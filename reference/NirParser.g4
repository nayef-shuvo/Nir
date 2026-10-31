parser grammar NirParser;

options {
    tokenVocab = NirLexer;
}

// A statement/declaration terminator: SEMI is emitted for both an explicit
// ';' and an inferred newline (see NirLexerBase), and the lexer already
// collapses runs of them (blank lines, comments between statements) down to
// at most one -- but a mix of explicit ';' and inferred ones can still stack
// up, so every terminator-consuming rule below accepts one or more.
terminator: SEMI+;

compilationUnit
    : terminator? (topLevelDeclaration (terminator topLevelDeclaration)*)? terminator? EOF
    ;

topLevelDeclaration
    : structDeclaration
    | functionDeclaration
    ;

structDeclaration
    : TYPE name=IDENTIFIER STRUCT LBRACE structField* RBRACE
    ;

structField
    : name=IDENTIFIER COLON type=typeReference COMMA
    ;

functionDeclaration
    : FN name=IDENTIFIER LPAREN parameterList? RPAREN (ARROW returnType=typeReference)? body=block
    ;

parameterList
    : parameter (COMMA parameter)*
    ;

parameter
    : name=IDENTIFIER COLON type=typeReference
    ;

// Primitive types (int, uint, double, ...) are plain identifiers here too --
// the lexer never distinguishes them, so neither does the grammar. A
// user-defined struct name is likewise just an identifier at this level.
typeReference
    : IDENTIFIER
    ;

block
    : LBRACE statementList RBRACE
    ;

statementList
    : terminator? (statement (terminator statement)*)? terminator?
    ;

statement
    : variableDeclarationStatement
    | returnStatement
    | ifStatement
    | forStatement
    | expressionStatement
    ;

variableDeclarationStatement
    : kind=(VAR | CONST) name=IDENTIFIER (COLON declaredType=typeReference)? EQ value=expression
    ;

returnStatement
    : RETURN value=expression?
    ;

ifStatement
    : IF condition=expression then=block (ELSE (elseIf=ifStatement | elseBlock=block))?
    ;

forStatement
    : FOR loopVariable=IDENTIFIER IN start=expression DOTDOT end=expression body=block
    ;

expressionStatement
    : expression
    ;

expression
    : left=expression op=(STAR | SLASH | PERCENT) right=expression    # multiplicativeExpression
    | left=expression op=(PLUS | MINUS) right=expression              # additiveExpression
    | left=expression op=(LT | LE | GT | GE) right=expression         # relationalExpression
    | left=expression op=(EQEQ | NEQ) right=expression                # equalityExpression
    | primaryExpression                                                # primaryExpr
    ;

primaryExpression
    : callee=IDENTIFIER LPAREN argumentList? RPAREN     # callExpression
    | LPAREN inner=expression RPAREN                     # parenthesizedExpression
    | arrayLiteral                                       # arrayLiteralExpr
    | interpolatedString                                 # interpolatedStringExpr
    | plainString                                        # plainStringExpr
    | IDENTIFIER                                         # identifierExpr
    | INT_LITERAL                                        # intLiteralExpr
    | UINT_LITERAL                                       # uintLiteralExpr
    | DOUBLE_LITERAL                                      # doubleLiteralExpr
    ;

// The trailing comma is optional for a call's arguments (LPAREN/RPAREN are
// depth-tracked by the lexer, so a multi-line call never needs one), but
// it's the only way to write a multi-line array literal: its element list
// is brace-delimited, and { } isn't depth-tracked -- see NirLexerBase.
argumentList
    : expression (COMMA expression)* COMMA?
    ;

arrayLiteral
    : LBRACKET RBRACKET elementType=typeReference LBRACE argumentList? RBRACE
    ;

plainString
    : STRING_START STRING_TEXT? STRING_END
    ;

interpolatedString
    : INTERP_STRING_START interpolatedStringPart* INTERP_STRING_END
    ;

interpolatedStringPart
    : INTERP_TEXT                     # interpolatedText
    | LBRACE expression RBRACE        # interpolatedHole
    ;
