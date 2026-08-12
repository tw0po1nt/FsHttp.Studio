// Fixture for the no-requests lens: damage between two blocks. A stray `}` is the probe's
// partial-loss case: parseFailed is true and one block survives, so the line-1 lens must not
// appear (docs/spec/0014-explain-missing-lenses.md, Decision 1). The URL is an inert loopback
// literal: the check only reads lenses, so this fixture needs no sidecar.

#r "nuget: FsHttp"

open FsHttp

http {
    GET "http://127.0.0.1:9/one"
}

}

http {
    GET "http://127.0.0.1:9/two"
}
