lexer grammar NirLexer;

options {
    // Statement-terminator inference from newlines and lexical error reporting
    // are hand-written C#, not grammar-expressible -- see NirLexerBase.
    superClass = NirLexerBase;
}

// ===================== Default mode: top-level source =====================

// Keywords must precede IDENTIFIER: ANTLR breaks same-length-match ties by rule order.
FN: 'fn';

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

IDENTIFIER: ID_START ID_CONTINUE*;

WS: [ \t]+ -> skip;
NEWLINE: ('\r'? '\n')+;
LINE_COMMENT: '//' ~[\r\n]* -> skip; // Nir has no block comments

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
