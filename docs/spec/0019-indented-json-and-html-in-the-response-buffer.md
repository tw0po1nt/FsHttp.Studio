# Indented JSON and HTML in the Response buffer

Spec for a change to the Body of the Response buffer in the Neovim client. It amends A8 of
[0015 The Neovim client](0015-neovim-client.md). A grilling session with the maintainer made each
decision below.

## Problem Statement

**A JSON body shows as one long line when Neovim has the `json` parser.** The client highlights
and folds the exact bytes of the body. A server often sends minified JSON, so the user gets one
long line with colors. The client pretty-prints a JSON body only when no parser exists. Thus a
user with the parser gets a worse body than a user without it. The VSCode Response viewer always
shows JSON as an indented tree.

**An HTML body shows as the exact source.** VSCode renders an HTML body as a page. A terminal
cannot render a page, so the Neovim client shows the source with highlights. Minified HTML is one
long line, and the source of a large page is hard to read.

## Solution

**The Response buffer indents each JSON body and each HTML body.**

- The JSON pretty-printer runs on each JSON body. When the `json` parser exists, tree-sitter runs
  on the pretty text for highlights and folds.
- A new pure Lua module indents an HTML body. Each block element starts a new line, one step
  deeper than its parent. Inline elements stay in their line of text. When the `html` parser
  exists, tree-sitter runs on the indented text. Without the parser, the module gives the folds.
- The Copy text of the Body and `:FsHttp open` keep the exact bytes.

## User Stories

1. As a Neovim user with the `json` parser, I want a minified JSON body pretty-printed, so that I can read it.
2. As a Neovim user with the `json` parser, I want tree-sitter highlights and folds on the pretty-printed body, so that I see the structure.
3. As a Neovim user, I want a JSON body that does not parse to show its exact bytes, so that I see what the server sent.
4. As a Neovim user, I want a minified HTML body indented, so that I can read the page structure.
5. As a Neovim user, I want inline elements such as `<a>` and `<b>` to stay in their line of text, so that a paragraph stays one line.
6. As a Neovim user, I want the content of `<pre>`, `<textarea>`, `<script>`, and `<style>` to stay exact, so that the indenter changes no meaningful whitespace and no code.
7. As a Neovim user, I want each tag to stay exactly as the server sent it, so that the attributes are what the server sent.
8. As a Neovim user, I want an HTML body with missing or stray close tags to show indented, so that a real page still reads well.
9. As a Neovim user with the `html` parser, I want tree-sitter highlights and folds on the indented body, so that HTML matches JSON.
10. As a Neovim user with no `html` parser, I want folds from the HTML structure, so that I can still close a large element.
11. As a Neovim user, I want `yb` to copy the exact bytes of the body, so that the Copy text is equal to the VSCode copy button.
12. As a Neovim user, I want `:FsHttp open` to open the exact page, so that the browser gets what the server sent.

## Implementation Decisions

### 1. JSON

- `add_body` in `response_view.lua` runs `json.pretty_print` on each JSON body first.
- When the pretty-printer succeeds, the body lookup for `json` gets the pretty text.
- With the parser, the highlights and folds come from tree-sitter. Without it, the folds come from
  the pretty-printer, as now.
- When the body does not parse as JSON, the client shows the exact bytes. The parser still gets
  those bytes, as now.
- The pretty-printer does not change. Its Golden fixture does not change.

### 2. HTML: the module

- A new core module, `fshttp.html_indent`, sits beside `fshttp.json`. It uses no `vim` global, so
  the Lua core suite loads it.
- It has one function. It takes the body text and returns the indented text and a list of folds,
  with the same fold shape as `json.pretty_print`.
- The function always returns text. A body that has no tag gives one line of text, after the
  whitespace rule of section 5.
- The client uses the module for `text/html` and `application/xhtml+xml`. XML does not change.
- There is no size limit. The JSON pretty-printer has no limit either.

### 3. HTML: lines and depth

- One indent step is two spaces, the same as the JSON pretty-printer.
- An inline element stays in its line of text. The inline elements are `a`, `abbr`, `b`, `bdi`,
  `bdo`, `br`, `button`, `cite`, `code`, `data`, `del`, `dfn`, `em`, `i`, `img`, `input`, `ins`,
  `kbd`, `label`, `mark`, `meter`, `output`, `picture`, `progress`, `q`, `s`, `samp`, `select`,
  `small`, `span`, `strong`, `sub`, `sup`, `svg`, `time`, `u`, `var`, and `wbr`.
- Each other element is a block element, custom elements included. A block element starts a new
  line at its depth.
- A block element that contains no block element stays on one line, for example
  `<h1>Snorlax</h1>` or `<p>Sleeps <b>a lot</b>.</p>`.
- A block element that contains a block element puts its open tag and its close tag on lines of
  their own. Its content goes one step deeper.
- An inline element that contains a block element gets the line rules of a block element, for
  example `<a href="/x"><div>card</div></a>`. For this rule, a comment, the doctype, and a raw text
  element are block elements.
- The match on an element name ignores letter case.

Example:

```html
<html>
  <body>
    <div class="card">
      <h1>Snorlax</h1>
      <p>Sleeps <b>a lot</b>, see <a href="/x">more</a>.</p>
      <ul>
        <li>rest</li>
        <li>snore</li>
      </ul>
    </div>
  </body>
</html>
```

### 4. HTML: text that stays exact

- **Each tag** stays byte for byte: the attribute order, the quotes, the letter case, and each
  line break inside the tag.
- **Raw text elements.** The open tag of `<pre>`, `<textarea>`, `<script>`, and `<style>` starts a
  new line, as a block element does. The content and the close tag follow it byte for byte, with no
  added line break and no indent.
- **A comment** goes on a line of its own, with its text unchanged.
- **The doctype** goes on a line of its own, with its text unchanged.
- **A processing instruction**, such as `<?xml version="1.0"?>`, goes on a line of its own, with
  its text unchanged.
- **A `<![CDATA[` section** stays unchanged, as text.
- **Character references** such as `&amp;` stay unchanged.

### 5. HTML: whitespace

- Outside the raw text elements, each run of whitespace in text becomes one space. Whitespace is a
  space, a tab, a carriage return, or a line feed.
- Text that is only whitespace, between two tags that start or end a line, is dropped.
- Text at the start or at the end of a line loses its leading or trailing space.
- In a block element on one line, the text directly after the open tag loses its leading space.
  The text directly before the close tag loses its trailing space. Thus `<li> snore </li>` gives
  `<li>snore</li>`, and a body that has an indent already gives the same lines as the minified body.
- A long line stays one line. The window sets `wrap`, `linebreak`, and `breakindent`.

### 6. HTML: a structure that does not balance

The indenter is tolerant, and it always gives indented text.

- A void element adds no depth: `area`, `base`, `br`, `col`, `embed`, `hr`, `img`, `input`,
  `link`, `meta`, `source`, `track`, and `wbr`. A self-closing tag such as `<x />` also adds no
  depth.
- A close tag closes back to its matching open tag. That also closes each open element between
  them.
- A close tag with no matching open tag stays in the output, and it changes no depth. A stray
  close tag of a block element goes on a line of its own. A stray close tag of an inline element
  stays in its line of text.
- An open element with no close tag stays open until the close tag of a parent closes it, or until
  the body ends. Thus an unclosed `<li>` shows one step too deep. This is a known limit.

### 7. HTML: highlights and folds

- When the `html` parser exists, the body lookup gets the indented text. Tree-sitter gives the
  highlights and the folds.
- Without the parser, the folds come from the indenter. Each element that spans more than one line
  gets a fold, from the line of its open tag to its last line.

### 8. What does not change

- The Copy text of the Body (`yb` and `:FsHttp yank body`) keeps the exact bytes. The Golden
  fixture of the Copy text does not change.
- `:FsHttp open` writes the exact bytes, with the CSP meta of spec 0015 A9.
- The hint line below the Body title does not change.
- The binary test and the image body do not change.

### 9. Changes to spec 0015

- A8: delete "The buffer keeps the exact bytes of the body. The client has no HTML formatter."
- A8, **Body text**: state the order of sections 1 and 7.
- Out of Scope: delete "An HTML formatter". Keep "HTML rendering in the terminal".
- Neovim suite: the JSON Check with the parser states the pretty-printed lines.

## Testing Decisions

### What makes a good test

A test asserts the lines and the folds that a user sees.

### Lua core suite

- `fshttp.html_indent`: a test for each rule of sections 3 to 6. The cases are minified input,
  input that has an indent already, inline elements in a paragraph, each raw text element, a
  comment, the doctype, a void element, a self-closing tag, a tag with line breaks between its
  attributes, a missing close tag, a stray close tag, upper-case tag names, a body with no tag, and
  the folds.
- `response_view` with a parser: the body lookup gets the pretty-printed JSON text, and the
  indented HTML text. The highlights and the folds of the lookup go on the body lines.
- `response_view` with no parser: an HTML body shows indented, with the folds of the indenter.
- The test "keeps the exact bytes of an HTML body and an XML body" changes. The XML half stays.

### Neovim suite

- The Check "a JSON body with the parser keeps its bytes" changes. It asserts the pretty-printed
  lines, with tree-sitter folds and highlights.
- A new Check: an HTML body with no `html` parser shows indented, with the folds of the indenter.
- The HTML reference screenshot shows the indented body. Write it again with
  `NVIM_TEST_SCREENSHOTS=update`, and examine the diff.
- The test HTTP server gives a minified HTML body for the new Check.

## Out of Scope

- A formatter for XML. VSCode shows XML as plain text too.
- A formatter for the JavaScript in `<script>` or the CSS in `<style>`.
- Implied close tags, such as a new `<li>` that closes the open `<li>`.
- A size limit for the JSON pretty-printer or the HTML indenter.
- An option that turns off the indent.
- A change to the VSCode Response viewer.

## Further Notes

### Tickets

- JSON: pretty-print before tree-sitter (sections 1 and 9, and the JSON tests).
- HTML: the indenter from the module to the Response buffer (sections 2 to 9, and the HTML tests).

The two tickets do not block each other. Both go in v0.3. The README text goes in the v0.3 README
ticket.
