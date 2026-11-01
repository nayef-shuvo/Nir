lexer grammar NirLexer;

tokens { HOLE_END }

options {
    // Statement-terminator inference from newlines and lexical error reporting
    // are hand-written C#, not grammar-expressible -- see NirLexerBase.
    superClass = NirLexerBase;
}

// ===================== Default mode: top-level source =====================

// Keywords must precede IDENTIFIER: ANTLR breaks same-length-match ties by rule order.
FN: 'fn';
LET: 'let';
MUT: 'mut';
RETURN: 'return';
USING: 'using';
IF: 'if';
ELSE: 'else';
WHILE: 'while';
FOR: 'for';
IN: 'in';
BREAK: 'break';
CONTINUE: 'continue';
AS: 'as';
TYPE: 'type';
STRUCT: 'struct';
UNION: 'union';
MATCH: 'match';
IMPL: 'impl';
SELF: 'self';
TRUE: 'true';
FALSE: 'false';

ARROW: '->';
FATARROW: '=>';
DOTDOTEQ: '..=';
DOTDOT: '..';
AMPAMP: '&&';
AMP: '&';
PIPEPIPE: '||';
BANG: '!';
EQEQ: '==';
NEQ: '!=';
LE: '<=';
GE: '>=';
LT: '<';
GT: '>';
PLUS: '+';
MINUS: '-';
STAR: '*';
SLASH: '/';
PERCENT: '%';

COLONCOLON: '::';
COLON: ':';
DOT: '.';
EQ: '=';
COMMA: ',';
LBRACE: '{';
RBRACE: '}';
LPAREN: '(';
RPAREN: ')';

// A statement terminator: either written explicitly, or inferred from a
// newline by NirLexerBase (which is also the only thing that ever looks at
// the NEWLINE token below -- it never reaches the parser as itself).
SEMI: ';';

// Entering a string switches lexer mode so `"` is read as text, not as its
// normal top-level meaning.
STRING_START: '"' -> pushMode(PLAIN_STRING);
INTERP_STRING_START: '$"' -> pushMode(INTERP_STRING);

DOUBLE_LITERAL: DIGIT+ '.' DIGIT+;
INT_LITERAL: DIGIT+;

IDENTIFIER: ID_START ID_CONTINUE*;

WS: [ \t]+ -> skip;
NEWLINE: ('\r'? '\n')+;
LINE_COMMENT: '//' ~[\r\n]* -> skip; // Nir has no block comments

fragment DIGIT: [0-9];
fragment ID_START: [a-zA-Z_];
fragment ID_CONTINUE: [a-zA-Z0-9_];
fragment ESCAPE: '\\' .;

// ===================== Plain string literal: "..." =========================
// Escape sequences (\n, \t, ...) are recognized only to know where the literal
// ends; decoding them is left to a later phase, so the raw text is kept.

mode PLAIN_STRING;
STRING_TEXT: (ESCAPE | ~["\\\r\n])+;
STRING_END: '"' -> popMode;
// A newline before the closing quote. NirLexerBase reports the error and turns
// this token into a STRING_END so the parser still sees a well-formed string.
UNTERMINATED_STRING: '\r'? '\n' -> popMode;

// ===================== Interpolated string: $"text {expression} text" ======
// The text between the holes is lexed here. A `{` opens a hole and goes back to the default mode, so the expression
// inside is lexed with the ordinary tokens. NirLexerBase counts braces to find the `}` that closes the hole, retypes
// it HOLE_END and pops back to this mode. Escapes are kept raw, as in plain strings; `{{` and `}}` are literal braces.

mode INTERP_STRING;
INTERP_TEXT: (ESCAPE | '{{' | '}}' | ~["{}\\\r\n])+;
HOLE_START: '{' -> pushMode(DEFAULT_MODE);
INTERP_STRING_END: '"' -> popMode;
// A newline before the closing quote; NirLexerBase reports it and turns it into INTERP_STRING_END.
INTERP_UNTERMINATED: '\r'? '\n' -> popMode;
