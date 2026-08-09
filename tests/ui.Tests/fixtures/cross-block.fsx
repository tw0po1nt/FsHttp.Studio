// Fixture for the cross-block Refused Run check. Two blocks: the first binds a value, the
// second reads that name and is otherwise a reachable, module-level block. FsHttp.Studio blanks
// every other block before it evaluates the setup, so the second block's Run is refused before
// any request is sent. The URLs are inert loopback literals on port 9. This fixture needs no
// sidecar and no live server.

#r "nuget: FsHttp"

open FsHttp

let dexId =
    http {
        GET "http://127.0.0.1:9/"
    }

http {
    GET "http://127.0.0.1:9/"
    header "X-Previous" (string dexId)
}
