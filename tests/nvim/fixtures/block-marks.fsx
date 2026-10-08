// A Script with two Blocks that a Run can reach. The Checks that open it start no Run, so each URL
// is an inert loopback literal.

#r "nuget: FsHttp"

open FsHttp

http { GET "http://127.0.0.1:9/one" }

http { GET "http://127.0.0.1:9/two" }
