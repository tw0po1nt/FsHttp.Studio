// Fixture for the Check that runs a Block whose Script loads a file that does not compile. The
// Setup stops at the Loaded file, so the Run never reaches the URL.

#r "nuget: FsHttp"
// FSI looks for a relative `#load` of the Script only in the include paths.
#I __SOURCE_DIRECTORY__
#load "loaded/broken.fsx"

open FsHttp

http { GET "http://127.0.0.1:9/" }
