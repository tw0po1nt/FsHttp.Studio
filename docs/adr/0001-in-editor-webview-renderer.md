# In-editor webview renderer

FsHttp.Studio is an F#/Fable extension. It renders responses in a VSCode webview panel inside the editor.

> **Update (Neovim client):** The Neovim client shows each Run's result in the Response buffer,
> inside the editor. A terminal cannot draw a rendered HTML page, and some terminals cannot draw an
> image. For these bodies only, `:FsHttp open` writes the body to a static file and opens it with
> the system handler. The user starts this action on demand. The Response buffer stays the result
> surface for each Run. See [docs/spec/0015](../spec/0015-neovim-client.md),
> decision A9.

We rejected a cheaper alternative: a custom FsHttp printer that writes an HTML file and opens it in the system browser. That alternative would deliver much of the rendering value with no extension, and it would work in any editor.

We rejected it because the in-editor experience is the point of the product. Only the extension can reach the intended explorer tree. A browser-file approach stays outside the editor permanently.

This decision is recorded so that a future session does not propose the browser-file shortcut again as an "obvious" simplification.
