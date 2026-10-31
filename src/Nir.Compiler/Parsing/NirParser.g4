parser grammar NirParser;

options {
    tokenVocab = NirLexer;
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
parameterList
    : parameter (COMMA parameter)* COMMA?
    ;

parameter
    : name=IDENTIFIER COLON type=IDENTIFIER
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
    : IF condition=expression then=block (terminator? ELSE (elseIf=ifStatement | elseBlock=block))?
    ;

whileStatement
    : WHILE condition=expression body=block
    ;

// The range belongs to `for`; it is not an expression of its own.
forStatement
    : FOR variable=IDENTIFIER IN start=expression op=(DOTDOT | DOTDOTEQ) end=expression body=block
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

assignmentStatement
    : target=IDENTIFIER EQ value=expression
    ;

// Alternatives are listed from highest to lowest precedence (ANTLR's rule for left-recursive rules), and all
// binary operators are left-associative.
expression
    : receiver=expression DOT method=IDENTIFIER LPAREN argumentList? RPAREN  # memberCallExpression
    | op=(MINUS | BANG) operand=expression                              # unaryExpression
    | left=expression op=(STAR | SLASH | PERCENT) right=expression      # multiplicativeExpression
    | left=expression op=(PLUS | MINUS) right=expression                # additiveExpression
    | left=expression op=(LT | LE | GT | GE) right=expression           # relationalExpression
    | left=expression op=(EQEQ | NEQ) right=expression                  # equalityExpression
    | left=expression op=AMPAMP right=expression                        # logicalAndExpression
    | left=expression op=PIPEPIPE right=expression                      # logicalOrExpression
    | callExpression                                                    # callExpr
    | LPAREN inner=expression RPAREN                                    # parenthesizedExpression
    | stringLiteral                                                     # stringExpr
    | INT_LITERAL                                                       # intExpr
    | DOUBLE_LITERAL                                                    # doubleExpr
    | TRUE                                                              # trueExpr
    | FALSE                                                             # falseExpr
    | IDENTIFIER                                                        # nameExpr
    ;

callExpression
    : callee=qualifiedName LPAREN argumentList? RPAREN
    ;

// A trailing comma is optional.
argumentList
    : expression (COMMA expression)* COMMA?
    ;

stringLiteral
    : STRING_START STRING_TEXT? STRING_END
    ;
