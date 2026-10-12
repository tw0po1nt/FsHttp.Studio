// A Script with one Block that a Run can reach, and one Block in a loop that a Run cannot reach. The
// Checks that open it start no Run, so each URL is an inert loopback literal.

#r "nuget: FsHttp"

open FsHttp

http { GET "http://127.0.0.1:9/one" }

for name in [ "two" ] do
    http { GET "http://127.0.0.1:9/two" }
