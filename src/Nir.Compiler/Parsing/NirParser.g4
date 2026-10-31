parser grammar NirParser;

options {
    tokenVocab = NirLexer;
}

@parser::members {
    // Greater than 0 while an `if` / `while` / `for` head is being parsed outside any parentheses. There `Name {` starts the
    // block, not a struct literal (Rust's rule), so a struct literal is not recognised. Parentheses, call arguments and
    // interpolation holes set it to 0 and restore it afterwards.
    private int _structLiteralsForbidden;
    private readonly Stack<int> _savedStructLiteralsForbidden = new();
}

// A statement/declaration terminator: SEMI is emitted for both an explicit ';'
// and an inferred newline (see NirLexerBase). The lexer already drops newlines
// that do not end a statement, but an explicit ';' followed by an inferred one
// can still stack up, so every place that consumes a terminator accepts one or more.
terminator
    : SEMI+
    ;

compilationUnit
    : terminator? (topLevelItem (terminator topLevelItem)*)? terminator? EOF
    ;

topLevelItem
    : usingDirective
    | functionDeclaration
    | structDeclaration
    | implDeclaration
    ;

// Fields are separated by a comma or a newline; the last separator is optional.
structDeclaration
    : TYPE name=IDENTIFIER EQ STRUCT LBRACE fieldList? RBRACE
    ;

fieldList
    : structField (separator structField)* separator?
    ;

structField
    : name=IDENTIFIER COLON type=IDENTIFIER
    ;

separator
    : COMMA
    | terminator
    ;

implDeclaration
    : typeName=IDENTIFIER IMPL LBRACE terminator? (functionDeclaration (terminator functionDeclaration)*)? terminator? RBRACE
    ;

// `using clr::cs::System::IO` imports one name; `using clr::cs::System::{Math, Console}` imports several.
// A multi-line group needs a trailing comma (see NirLexerBase: braces are not depth-tracked).
usingDirective
    : USING importPath
    ;

importPath
    : IDENTIFIER (COLONCOLON IDENTIFIER)* (COLONCOLON importGroup)?
    ;

importGroup
    : LBRACE IDENTIFIER (COMMA IDENTIFIER)* COMMA? RBRACE
    ;

qualifiedName
    : IDENTIFIER (COLONCOLON IDENTIFIER)*
    ;

functionDeclaration
    : FN name=IDENTIFIER LPAREN parameterList? RPAREN (ARROW returnType=IDENTIFIER)? body=block
    ;

// A trailing comma is optional. Types are plain identifiers; which names are types is decided by the binder.
// A method may start with a receiver, `&self` or `&mut self`; the binder rejects it outside an `impl`.
parameterList
    : receiverParameter (COMMA parameter)* COMMA?
    | parameter (COMMA parameter)* COMMA?
    ;

receiverParameter
    : AMP MUT? SELF
    ;

parameter
    : name=IDENTIFIER COLON type=typeReference
    ;

// A parameter may be a borrow: `&Point` or `&mut Point`.
typeReference
    : AMP MUT? name=IDENTIFIER
    | name=IDENTIFIER
    ;

block
    : LBRACE statementList RBRACE
    ;

statementList
    : terminator? (statement (terminator statement)*)? terminator?
    ;

statement
    : variableDeclaration
    | assignmentStatement
    | returnStatement
    | ifStatement
    | whileStatement
    | forStatement
    | breakStatement
    | continueStatement
    | expression
    ;

// The optional terminator before ELSE lets `else` start on the line after the closing brace.
ifStatement
    : IF {_structLiteralsForbidden++;} condition=expression {_structLiteralsForbidden--;} then=block (terminator? ELSE (elseIf=ifStatement | elseBlock=block))?
    ;

whileStatement
    : WHILE {_structLiteralsForbidden++;} condition=expression {_structLiteralsForbidden--;} body=block
    ;

// The range belongs to `for`; it is not an expression of its own.
forStatement
    : FOR variable=IDENTIFIER IN {_structLiteralsForbidden++;} start=expression op=(DOTDOT | DOTDOTEQ) end=expression {_structLiteralsForbidden--;} body=block
    ;

breakStatement
    : BREAK
    ;

continueStatement
    : CONTINUE
    ;

returnStatement
    : RETURN value=expression?
    ;

// A type annotation is a plain identifier; which names are types is decided by the binder.
variableDeclaration
    : LET MUT? name=IDENTIFIER (COLON declaredType=IDENTIFIER)? EQ value=expression
    ;

// A name, `self`, or a path of fields: `x`, `p.x`, `self.pos.x`.
assignmentStatement
    : target=assignmentTarget EQ value=expression
    ;

assignmentTarget
    : (IDENTIFIER | SELF) (DOT IDENTIFIER)*
    ;

// Alternatives are listed from highest to lowest precedence (ANTLR's rule for left-recursive rules), and all
// binary operators are left-associative.
expression
    : receiver=expression DOT method=IDENTIFIER LPAREN argumentList? RPAREN  # memberCallExpression
    | receiver=expression DOT field=IDENTIFIER                          # fieldAccessExpression
    | op=(MINUS | BANG) operand=expression                              # unaryExpression
    | AMP MUT? operand=expression                                       # referenceExpression
    | operand=expression AS targetType=IDENTIFIER                       # castExpression
    | left=expression op=(STAR | SLASH | PERCENT) right=expression      # multiplicativeExpression
    | left=expression op=(PLUS | MINUS) right=expression                # additiveExpression
    | left=expression op=(LT | LE | GT | GE) right=expression           # relationalExpression
    | left=expression op=(EQEQ | NEQ) right=expression                  # equalityExpression
    | left=expression op=AMPAMP right=expression                        # logicalAndExpression
    | left=expression op=PIPEPIPE right=expression                      # logicalOrExpression
    | callExpression                                                    # callExpr
    | LPAREN {_savedStructLiteralsForbidden.Push(_structLiteralsForbidden); _structLiteralsForbidden = 0;}
      inner=expression {_structLiteralsForbidden = _savedStructLiteralsForbidden.Pop();} RPAREN   # parenthesizedExpression
    | stringLiteral                                                     # stringExpr
    | interpolatedString                                                # interpolatedStringExpr
    | INT_LITERAL                                                       # intExpr
    | DOUBLE_LITERAL                                                    # doubleExpr
    | TRUE                                                              # trueExpr
    | FALSE                                                             # falseExpr
    | SELF                                                              # selfExpr
    | {_structLiteralsForbidden == 0}? typeName=IDENTIFIER LBRACE fieldInitializerList? RBRACE   # structLiteralExpr
    | IDENTIFIER                                                        # nameExpr
    ;

// `Point { x = 1, y = 2 }`: every field once, separated by commas or newlines.
fieldInitializerList
    : fieldInitializer (separator fieldInitializer)* separator?
    ;

fieldInitializer
    : name=IDENTIFIER EQ value=expression
    ;

callExpression
    : callee=qualifiedName LPAREN argumentList? RPAREN
    ;

// A trailing comma is optional.
argumentList
    : {_savedStructLiteralsForbidden.Push(_structLiteralsForbidden); _structLiteralsForbidden = 0;}
      expression (COMMA expression)* COMMA?
      {_structLiteralsForbidden = _savedStructLiteralsForbidden.Pop();}
    ;

stringLiteral
    : STRING_START STRING_TEXT? STRING_END
    ;

// $"text {expression} text". The lexer turns the braces that open and close a hole into HOLE_START and HOLE_END.
interpolatedString
    : INTERP_STRING_START interpolatedPart* INTERP_STRING_END
    ;

interpolatedPart
    : INTERP_TEXT                       # interpolatedText
    | HOLE_START {_savedStructLiteralsForbidden.Push(_structLiteralsForbidden); _structLiteralsForbidden = 0;}
      expression {_structLiteralsForbidden = _savedStructLiteralsForbidden.Pop();} HOLE_END   # interpolatedHole
    ;
