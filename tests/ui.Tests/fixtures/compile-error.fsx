// Fixture for the Compile Error names its source check. One reachable module-level block, and
// above it a stable region the check breaks with a type error. The URL is an inert loopback
// literal: a Setup that does not compile never reaches the URL, so this fixture needs no
// sidecar and no live server.

#r "nuget: FsHttp"

open FsHttp

// Break-target: the check replaces this line with a type error. Keep this comment on the line
// above the target so the target's line number stays obvious and stable.
let probe = 0

http { GET "http://127.0.0.1:9/" }
