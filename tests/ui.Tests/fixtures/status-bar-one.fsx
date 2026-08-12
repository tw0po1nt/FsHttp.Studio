// Fixture for the document-aware status bar: one `http` block. Locate never reaches a Run, so
// the URL is an inert loopback literal and this fixture needs no sidecar.

#r "nuget: FsHttp"

open FsHttp

http { GET "http://127.0.0.1:9/" }
