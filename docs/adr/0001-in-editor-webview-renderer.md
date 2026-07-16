# In-editor webview renderer, not a browser-file printer

FsHttp.Explorer renders responses in a VSCode webview panel inside the editor, built as an F#/Fable extension. We rejected the cheaper alternative — a custom FsHttp printer that writes an HTML file and shells out to the system browser — even though it would deliver much of the rendering value with no extension at all and work in any editor.

The in-editor experience is the whole point: the extension is the only vehicle that ever reaches the intended explorer tree, and a browser-file approach would permanently live outside the editor. Recorded so a future session doesn't re-propose the browser-file shortcut as an "obvious" simplification.
