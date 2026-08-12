// Fixture for the document-aware status bar: two `http` blocks. Locate never reaches a Run, so
// the URL is an inert loopback literal and this fixture needs no sidecar.

#r "nuget: FsHttp"

open FsHttp

http { GET "http://127.0.0.1:9/one" }

http { GET "http://127.0.0.1:9/two" }
