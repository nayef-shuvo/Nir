lexer grammar NirLexer;

// This grammar is also linked into Nir.Syntax (see its .csproj) to
// regenerate a throwaway copy of the token vocabulary there, under a
// different namespace. The 'superClass' option below can only take a plain
// identifier, not a qualified name, so this import is what lets that
// unqualified NirLexerBase resolve there too -- in the real build (this
// project), it's a harmless no-op since NirLexerBase already lives in this
// same namespace.
@lexer::header {
using Nir.Lexer;
}

options {
    // ASI (statement-terminator inference from newlines) is hand-written C#,
    // not grammar-expressible -- see NirLexerBase for the rules.
    superClass = NirLexerBase;
}

// ===================== Default mode: top-level source =====================

// Keywords (must precede IDENTIFIER: ANTLR breaks same-length-match ties by rule order).
FN: 'fn';
VAR: 'var';
CONST: 'const';
TYPE: 'type';
STRUCT: 'struct';
RETURN: 'return';
IF: 'if';
ELSE: 'else';
FOR: 'for';
IN: 'in';

ARROW: '->';
DOTDOT: '..';
EQEQ: '==';
NEQ: '!=';
LE: '<=';
GE: '>=';

DOT: '.';
COLON: ':';
COMMA: ',';
EQ: '=';
LT: '<';
GT: '>';
PLUS: '+';
MINUS: '-';
STAR: '*';
SLASH: '/';
PERCENT: '%';

LBRACE: '{';
RBRACE: '}';
LPAREN: '(';
RPAREN: ')';
LBRACKET: '[';
RBRACKET: ']';

// A statement terminator: either written explicitly, or inferred from a
// newline by NirLexerBase (which is also the only thing that ever looks at
// the NEWLINE token below -- it never reaches the parser as itself).
SEMI: ';';

// Entering a string switches lexer mode so `"` and `{` are read as text/markers,
// not as their normal top-level meaning.
INTERP_STRING_START: '$"' -> pushMode(INTERP_STRING);
STRING_START: '"' -> pushMode(PLAIN_STRING);

DOUBLE_LITERAL: DIGIT+ '.' DIGIT+;
UINT_LITERAL: DIGIT+ [uU];
INT_LITERAL: DIGIT+;

IDENTIFIER: ID_START ID_CONTINUE*;

WS: [ \t]+ -> skip;
// Never reaches the parser: NirLexerBase.NextToken() turns each run of
// newlines into either a SEMI (statement terminator) or nothing, depending
// on bracket depth and the preceding token. See NirLexerBase for the rules.
NEWLINE: ('\r'? '\n')+;
LINE_COMMENT: '//' ~[\r\n]* -> skip; // covers both `//` and `///`, Nir has no block comments

fragment DIGIT: [0-9];
fragment ID_START: [a-zA-Z_];
fragment ID_CONTINUE: [a-zA-Z0-9_];
fragment ESCAPE: '\\' .;

// ===================== Plain string literal: "..." =========================
// Escape decoding (\n, \t, ...) is left to a later phase; the lexer only needs
// to know where the literal ends, so raw text (including backslashes) is kept.

mode PLAIN_STRING;
STRING_TEXT: (ESCAPE | ~["\\\r\n])+;
STRING_END: '"' -> popMode;

// ===================== Interpolated string text: $"...text...{expr}..." ====

mode INTERP_STRING;
INTERP_TEXT: (ESCAPE | '{{' | '}}' | ~["{}\\\r\n])+;
INTERP_BRACE_OPEN: '{' -> type(LBRACE), pushMode(INTERP_EXPR);
INTERP_STRING_END: '"' -> popMode;

// ===================== Inside the `{ expr }` of an interpolation ===========
// Mirrors the default-mode token set (an interpolation only ever holds an
// expression) so ANTLR's own mode stack -- not a hand-rolled counter -- tracks
// brace nesting: `{` pushes this mode again, `}` pops back to it, and the
// outermost `}` pops all the way back to INTERP_STRING to resume string text.

mode INTERP_EXPR;

IE_FN: 'fn' -> type(FN);
IE_VAR: 'var' -> type(VAR);
IE_CONST: 'const' -> type(CONST);
IE_TYPE: 'type' -> type(TYPE);
IE_STRUCT: 'struct' -> type(STRUCT);
IE_RETURN: 'return' -> type(RETURN);
IE_IF: 'if' -> type(IF);
IE_ELSE: 'else' -> type(ELSE);
IE_FOR: 'for' -> type(FOR);
IE_IN: 'in' -> type(IN);

IE_ARROW: '->' -> type(ARROW);
IE_DOTDOT: '..' -> type(DOTDOT);
IE_EQEQ: '==' -> type(EQEQ);
IE_NEQ: '!=' -> type(NEQ);
IE_LE: '<=' -> type(LE);
IE_GE: '>=' -> type(GE);

IE_DOT: '.' -> type(DOT);
IE_COLON: ':' -> type(COLON);
IE_COMMA: ',' -> type(COMMA);
IE_EQ: '=' -> type(EQ);
IE_LT: '<' -> type(LT);
IE_GT: '>' -> type(GT);
IE_PLUS: '+' -> type(PLUS);
IE_MINUS: '-' -> type(MINUS);
IE_STAR: '*' -> type(STAR);
IE_SLASH: '/' -> type(SLASH);
IE_PERCENT: '%' -> type(PERCENT);

IE_LPAREN: '(' -> type(LPAREN);
IE_RPAREN: ')' -> type(RPAREN);
IE_LBRACKET: '[' -> type(LBRACKET);
IE_RBRACKET: ']' -> type(RBRACKET);

IE_LBRACE: '{' -> type(LBRACE), pushMode(INTERP_EXPR);
IE_RBRACE: '}' -> type(RBRACE), popMode;

IE_INTERP_STRING_START: '$"' -> type(INTERP_STRING_START), pushMode(INTERP_STRING);
IE_STRING_START: '"' -> type(STRING_START), pushMode(PLAIN_STRING);

IE_DOUBLE_LITERAL: DIGIT+ '.' DIGIT+ -> type(DOUBLE_LITERAL);
IE_UINT_LITERAL: DIGIT+ [uU] -> type(UINT_LITERAL);
IE_INT_LITERAL: DIGIT+ -> type(INT_LITERAL);

IE_IDENTIFIER: ID_START ID_CONTINUE* -> type(IDENTIFIER);

IE_WS: [ \t]+ -> skip;
