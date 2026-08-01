# In-editor webview renderer, not a browser-file printer

FsHttp.Studio is an F#/Fable extension. It renders responses in a VSCode webview panel inside the editor.

We rejected a cheaper alternative: a custom FsHttp printer that writes an HTML file and opens it in the system browser. That alternative would deliver much of the rendering value with no extension, and it would work in any editor.

We rejected it because the in-editor experience is the point of the product. Only the extension can reach the intended explorer tree. A browser-file approach stays outside the editor permanently.

This decision is recorded so that a future session does not propose the browser-file shortcut again as an "obvious" simplification.
