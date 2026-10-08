module Extension.Tests.SdkFloorGoldenTests

open System.IO
open Expecto
open Companion.Envelope
open Protocol

let private floorCases =
    [ "the version of a .NET 10 companion", Some "10.0.0", 10
      "a newer major version", Some "11.0.0", 11
      "a preview version", Some "11.0.0-preview.7.25380.108", 11
      "a version with no minor part", Some "12", 12
      "no version", None, fallbackSdkFloor
      "an empty version", Some "", fallbackSdkFloor
      "a major version that is not a number", Some "x.0", fallbackSdkFloor
      "a major version with a sign", Some "+10.0.0", fallbackSdkFloor ]

let private listSdksCases =
    [ "an SDK at the floor", 10, "8.0.404 [/usr/local/share/dotnet/sdk]\n10.0.201 [/usr/local/share/dotnet/sdk]\n", true
      "an SDK above the floor only", 10, "11.0.100-rc.1.26425.128 [/usr/local/share/dotnet/sdk]\n", true
      "each SDK below the floor", 10, "8.0.404 [/sdk]\n9.0.100 [/sdk]\n", false
      "Windows line ends", 10, "10.0.100 [C:\\Program Files\\dotnet\\sdk]\r\n", true
      "spaces before the version", 10, "  10.0.100 [/sdk]\n", true
      "a tab after the version", 10, "10.0.100\t[/sdk]\n", true
      "no output", 10, "", false
      "blank lines", 10, "\n  \n", false
      "a major version that is not a number", 10, "10abc.0.1 [/sdk]\n", false ]

let private goldenFixture () =
    encode
        {| floorCases =
            [ for name, frameworkVersion, _ in floorCases ->
                  {| floor = sdkFloor frameworkVersion
                     frameworkVersion = Option.toObj frameworkVersion
                     name = name |} ]
           listSdksCases =
            [ for name, floor, listSdksOutput, _ in listSdksCases ->
                  {| floor = floor
                     hasSdkAtFloor = hasSdkAtFloor floor listSdksOutput
                     listSdksOutput = listSdksOutput
                     name = name |} ] |}

[<Tests>]
let tests =
    testList
        "SDK floor Golden fixture"
        [ for name, frameworkVersion, expected in floorCases do
              test (sprintf "%s gives the SDK floor %d" name expected) {
                  Expect.equal (sdkFloor frameworkVersion) expected name
              }

          for name, floor, listSdksOutput, expected in listSdksCases do
              test (sprintf "%s gives %b at the floor %d" name expected floor) {
                  Expect.equal (hasSdkAtFloor floor listSdksOutput) expected name
              }

          test "the SDK floor rule matches its Golden fixture" {
              GoldenFixture.verify (Path.Combine("sdk", "sdk-floor.json")) (goldenFixture ())
          } ]
