// Fixture for the no-requests lens: damage above every block. An unterminated string before
// the first `http` block makes the locator report parseFailed with no range, which is the pair
// that paints the line-1 lens (docs/spec/0014-explain-missing-lenses.md, Decision 1). The URL is
// an inert loopback literal: locate never reaches a Run, so this fixture needs no sidecar.

#r "nuget: FsHttp"

open FsHttp

let s = "oops
http {
    GET "http://127.0.0.1:9/"
}
