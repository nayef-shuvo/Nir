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
    | tupleAliasDeclaration
    | unionDeclaration
    | implDeclaration
    ;

// Fields are separated by a comma or a newline; the last separator is optional.
structDeclaration
    : TYPE name=IDENTIFIER EQ kind=(STRUCT | ERROR) LBRACE fieldList? RBRACE
    ;

fieldList
    : structField (separator structField)* separator?
    ;

structField
    : name=IDENTIFIER COLON type=typeSyntax
    ;

// `type Pair = (x: int, y: int)`: a name for a tuple type.
tupleAliasDeclaration
    : TYPE name=IDENTIFIER EQ tupleType
    ;

// `type Shape = union { Circle, Square }`: members are separated by a comma or a newline.
unionDeclaration
    : TYPE name=IDENTIFIER EQ UNION LBRACE unionMember (separator unionMember)+ separator? RBRACE
    ;

unionMember
    : type=typeSyntax
    ;

// A type is a plain identifier or a tuple type. Which names are types is decided by the binder.
// `E!T` is "an error of type E, or a T"; `E!` is "an error of type E, or nothing".
typeSyntax
    : tupleType
    | name=IDENTIFIER (BANG success=typeSyntax?)?
    ;

// Two or more elements, each optionally named: `(int, double)`, `(x: int, y: int)`.
tupleType
    : LPAREN tupleTypeElement (COMMA tupleTypeElement)+ COMMA? RPAREN
    ;

tupleTypeElement
    : (name=IDENTIFIER COLON)? type=typeSyntax
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
    : FN name=IDENTIFIER LPAREN parameterList? RPAREN (ARROW returnType=typeSyntax)? body=block
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
    : name=IDENTIFIER COLON type=parameterType
    ;

// A parameter may be a borrow: `&Point` or `&mut Point`.
parameterType
    : AMP MUT? name=IDENTIFIER
    | typeSyntax
    ;

block
    : LBRACE statementList RBRACE
    ;

statementList
    : terminator? (statement (terminator statement)*)? terminator?
    ;

statement
    : destructuringDeclaration
    | variableDeclaration
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
    : LET MUT? name=IDENTIFIER (COLON declaredType=typeSyntax)? EQ value=expression
    ;

// `let (a, mut b) = pair`: each name takes one element of a tuple.
destructuringDeclaration
    : LET LPAREN destructuredName (COMMA destructuredName)+ COMMA? RPAREN EQ value=expression
    ;

destructuredName
    : MUT? name=IDENTIFIER
    ;

// A name, `self`, or a path of fields and tuple indexes: `x`, `p.x`, `self.pos.x`, `t.0`, `t.0.1`.
// (`0.1` is one DOUBLE_LITERAL token; the syntax tree builder splits it.)
assignmentStatement
    : target=assignmentTarget EQ value=expression
    ;

assignmentTarget
    : (IDENTIFIER | SELF) (DOT (IDENTIFIER | INT_LITERAL | DOUBLE_LITERAL))*
    ;

// Alternatives are listed from highest to lowest precedence (ANTLR's rule for left-recursive rules), and all
// binary operators are left-associative.
expression
    // Listed first so that `error Name { ... }` is an error literal and not `error` applied to a struct literal.
    : {_structLiteralsForbidden == 0}? ERROR typeName=IDENTIFIER LBRACE fieldInitializerList? RBRACE   # errorLiteralExpr
    | receiver=expression DOT method=IDENTIFIER LPAREN argumentList? RPAREN  # memberCallExpression
    | receiver=expression DOT field=IDENTIFIER                          # fieldAccessExpression
    | receiver=expression DOT index=(INT_LITERAL | DOUBLE_LITERAL)      # tupleIndexExpression
    | op=(MINUS | BANG) operand=expression                              # unaryExpression
    | TRY operand=expression                                            # tryExpression
    | ERROR message=expression                                          # errorMessageExpression
    | AMP MUT? operand=expression                                       # referenceExpression
    | operand=expression AS targetType=IDENTIFIER                       # castExpression
    | left=expression op=(STAR | SLASH | PERCENT) right=expression      # multiplicativeExpression
    | left=expression op=(PLUS | MINUS) right=expression                # additiveExpression
    | left=expression op=(LT | LE | GT | GE) right=expression           # relationalExpression
    | left=expression op=(EQEQ | NEQ) right=expression                  # equalityExpression
    | left=expression op=AMPAMP right=expression                        # logicalAndExpression
    | left=expression op=PIPEPIPE right=expression                      # logicalOrExpression
    | left=expression CATCH name=IDENTIFIER FATARROW handler=expression # catchHandlerExpression
    | left=expression CATCH name=IDENTIFIER FATARROW body=block         # catchBlockExpression
    | left=expression CATCH fallback=expression                         # catchFallbackExpression
    | callExpression                                                    # callExpr
    | MATCH {_structLiteralsForbidden++;} scrutinee=expression {_structLiteralsForbidden--;} LBRACE
      {_savedStructLiteralsForbidden.Push(_structLiteralsForbidden); _structLiteralsForbidden = 0;}
      matchArm (separator matchArm)* separator?
      {_structLiteralsForbidden = _savedStructLiteralsForbidden.Pop();} RBRACE                   # matchExpr
    | LPAREN {_savedStructLiteralsForbidden.Push(_structLiteralsForbidden); _structLiteralsForbidden = 0;}
      elements+=expression (COMMA elements+=expression)+ COMMA? {_structLiteralsForbidden = _savedStructLiteralsForbidden.Pop();} RPAREN   # tupleLiteralExpr
    | LPAREN {_savedStructLiteralsForbidden.Push(_structLiteralsForbidden); _structLiteralsForbidden = 0;}
      inner=expression {_structLiteralsForbidden = _savedStructLiteralsForbidden.Pop();} RPAREN   # parenthesizedExpression
    | TYPEOF LPAREN {_savedStructLiteralsForbidden.Push(_structLiteralsForbidden); _structLiteralsForbidden = 0;}
      typeofOperand {_structLiteralsForbidden = _savedStructLiteralsForbidden.Pop();} RPAREN   # typeofExpr
    | stringLiteral                                                     # stringExpr
    | interpolatedString                                               # interpolatedStringExpr
    | INT_LITERAL                                                       # intExpr
    | DOUBLE_LITERAL                                                    # doubleExpr
    | CHAR_LITERAL                                                      # charExpr
    | TRUE                                                              # trueExpr
    | FALSE                                                             # falseExpr
    | SELF                                                              # selfExpr
    | {_structLiteralsForbidden == 0}? typeName=IDENTIFIER LBRACE fieldInitializerList? RBRACE   # structLiteralExpr
    | IDENTIFIER                                                        # nameExpr
    ;

// What `typeof(...)` looks at. A type is tried first, so a plain name is read as a type syntax and the binder decides whether it
// is a variable or a type; anything that is not a type (`x + 1`, `p.x`, `5`) is an expression.
typeofOperand
    : type=typeSyntax
    | value=expression
    ;

// `c: Circle => expression` or `c: Circle => { statements }`. The name may be `_` to drop the value.
matchArm
    : name=IDENTIFIER COLON type=typeSyntax FATARROW (body=block | value=expression)
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
