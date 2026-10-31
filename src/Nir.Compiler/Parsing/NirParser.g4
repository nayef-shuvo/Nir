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
    : terminator? (functionDeclaration (terminator functionDeclaration)*)? terminator? EOF
    ;

functionDeclaration
    : FN name=IDENTIFIER LPAREN RPAREN body=block
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
    | expression
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
    : op=MINUS operand=expression                                       # unaryExpression
    | left=expression op=(STAR | SLASH | PERCENT) right=expression      # multiplicativeExpression
    | left=expression op=(PLUS | MINUS) right=expression                # additiveExpression
    | left=expression op=(LT | LE | GT | GE) right=expression           # relationalExpression
    | left=expression op=(EQEQ | NEQ) right=expression                  # equalityExpression
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
    : callee=IDENTIFIER LPAREN argumentList? RPAREN
    ;

// A trailing comma is optional.
argumentList
    : expression (COMMA expression)* COMMA?
    ;

stringLiteral
    : STRING_START STRING_TEXT? STRING_END
    ;
