// dotnet build
// dotnet run -r rootPath

open System
open DT2SG_lib
open CommandLine
open CommandLine.Text

type options = {
  [<Option('r', "root", Required = true, HelpText = "Path of the root of Directory Tree")>] root_string: string;
  [<Option('m', "metadata", Required = true, HelpText = "Path of the metadata file")>] metadata_string: string;
 // [<Option('i', "ignore", Required = true, HelpText = "Path of the ignore list file")>] ignore_string: string;
 // [<Option('c', "committer_name", Required = true, HelpText = "Name and surname of Committer")>] commiter_name: string;
 // [<Option('e', "committer_email", Required = true, HelpText = "Email of Committer")>] committer_email: string;
}


[<EntryPoint>]
let main argv =

    let mutable root_path, metadata_path, ignore_path, committer_name, committer_email = "", "", "", "", ""
    let run (a: CommandLine.Parsed<options>) =
        root_path <- a.Value.root_string
        metadata_path <- a.Value.metadata_string
        //ignore_path <- a.Value.ignore_string
        //committer_email <- a.Value.committer_email
        //committer_name <- a.Value.commiter_name

    let fail a =
        Console.WriteLine(a.ToString())


    let result = CommandLine.Parser.Default.ParseArguments<options>(argv)
    match result with
        | :? Parsed<options> as parsed -> run parsed //.Value
        | :? NotParsed<options> as notParsed -> fail notParsed.Errors
        | _ -> () // you should not be here

    Lib.createSyntheticGit(root_path, metadata_path, ignore_path)
    Console.WriteLine("You Git is in " + root_path )

    0 // return an integer exit code
