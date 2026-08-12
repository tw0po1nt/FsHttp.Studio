// Fixture for the no-requests lens: damage below every block. A trailing incomplete binding
// keeps each earlier block (docs/spec/0014-explain-missing-lenses.md, Decision 1), so the
// line-1 lens must not appear. The URL is an inert loopback literal: the check only reads
// lenses, so this fixture needs no sidecar.

#r "nuget: FsHttp"

open FsHttp

http {
    GET "http://127.0.0.1:9/one"
}

http {
    GET "http://127.0.0.1:9/two"
}

let c =
