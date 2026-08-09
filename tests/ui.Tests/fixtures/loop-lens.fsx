// Fixture for the loop-lens check. One block inside a `for` loop — the `loopBody` refusal
// shape — and nothing else a Run could reach. The URL is an inert loopback literal: a refused
// block is never evaluated, so this fixture needs no sidecar and no live server.

#r "nuget: FsHttp"

open FsHttp

for name in [ "pidgey" ] do
    http { GET "http://127.0.0.1:9/" }
