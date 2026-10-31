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
    : expression
    ;

expression
    : callExpression
    | stringLiteral
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
