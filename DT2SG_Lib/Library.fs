namespace DT2SG_lib

open FSharp.Data
open FSharp.Data.CsvExtensions
open LibGit2Sharp
open System
open System.IO

module Lib =

    let row_directory_name = "directory name"
    let row_author_name = "author name"
    let row_author_email = "author email"
    let row_date = "date"
    let row_curator_name = "curator name"
    let row_curator_email = "curator email"
    let row_commit_message = "commit message"
    let row_release = "release tag"    

    // date format into author.csv
    let date_format = "MM'/'dd'/'yyyy HH':'mm"

    let none (a) = if String.IsNullOrWhiteSpace(a) then "n.d." else a

    let filter_git_files = fun (path: string) -> not(path.Contains(".git"))

    let rec directoryCopy srcPath dstPath copySubDirs =
        try
            if not <| System.IO.Directory.Exists(srcPath) then
                let msg = System.String.Format("Source directory does not exist or could not be found: {0}", srcPath)
                raise (System.IO.DirectoryNotFoundException(msg))
            if not <| System.IO.Directory.Exists(dstPath) then System.IO.Directory.CreateDirectory(dstPath) |> ignore
            let srcDir = new System.IO.DirectoryInfo(srcPath)
            for file in srcDir.GetFiles() do
                let temppath = System.IO.Path.Combine(dstPath, file.Name)
                let b = FileInfo(temppath)
                //if b.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint) //TODO: here if symbolic link do not copy; todo: copy symbolic link without resolving it
                //    then ()
                //    else 
                try 
                    file.CopyTo(temppath, true) |> ignore 
                with 
                    e -> ()
        
            
            if copySubDirs then
                for subdir in srcDir.GetDirectories() do
                    let dstSubDir = System.IO.Path.Combine(dstPath, subdir.Name)
                    directoryCopy subdir.FullName dstSubDir copySubDirs
        with 
                    e -> ()

    let initGit (root_path,branch_name,committer_name, committer_email) =
        // find git going up
        let gitPath = Repository.Discover(root_path)
        let repo = 
            if isNull(gitPath) then new Repository(root_path) else new Repository(gitPath)
        let branches = repo.Branches
        let mutable exist_SRC_branch = false;
        let mutable src_branch = repo.Head
        for b in branches do
         if b.FriendlyName = branch_name
            then
                exist_SRC_branch <- true
                src_branch <- b
        done
        if not(exist_SRC_branch)
            then
                let master = repo.Branches.["master"]
                let emptyCommit = Array.empty<Commit>
                let treeDefinition = new TreeDefinition()
                let tree = repo.ObjectDatabase.CreateTree(treeDefinition)
                let empty_sig = new Signature(committer_name, committer_email, DateTimeOffset.Now )
                let commit = repo.ObjectDatabase.CreateCommit(empty_sig, empty_sig, "empty commit", tree, emptyCommit, false)
                src_branch <- repo.Branches.Add(branch_name, commit)
        repo
    let executeInBash(command:string, path) =
        let mutable result = ""
        use  proc = new System.Diagnostics.Process()
        (
        //proc.StartInfo.FileName <- "/bin/bash";
        //proc.StartInfo.Arguments <- "-c \" " + command + " \"";
        proc.StartInfo.FileName <- "cmd.exe";

        proc.StartInfo.UseShellExecute <- false;
        proc.StartInfo.RedirectStandardOutput <- true;
        proc.StartInfo.RedirectStandardInput <- true;
        proc.StartInfo.RedirectStandardError <- true;
        proc.StartInfo.WorkingDirectory <- path
        proc.Start() |> ignore

        proc.StandardInput.WriteLine(command)
        proc.StandardInput.Flush()
        proc.StandardInput.Close()

        result <- result + proc.StandardOutput.ReadToEnd();
        result <- result +  proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        )
        result

    let fixPre1970Commit(author_date:DateTimeOffset, bad_date:DateTimeOffset, message, path, branch) = 
        //fix dates as http://git.661346.n2.nabble.com/Back-dating-commits-way-back-for-constitution-git-td5365303.html#a5365346
        //save commit object in a file, to be edited 
        //$git cat-file -p HEAD > tmp.txt 
        //edit tmp.txt, changing sign of author time
        //$ [edit tmp.txt] 
        //replace just created commit by handcrafted one 
        //$ git reset --hard HEAD^ 
        //$ git hash-object -t commit -w tmp.txt
        //$ git update-ref -m 'commit: foo' refs/heads/master \ 
        //fa5e5a2b6f27f10ce920ca82ffef07ed3eb3f26f 
        let toReplace = bad_date.ToUnixTimeSeconds().ToString()
        let replaceWith = author_date.ToUnixTimeSeconds().ToString()
        let mutable command = "git cat-file -p HEAD"
        let out = executeInBash(command, path)
        let corrected = out.Replace(toReplace, replaceWith)
        command <- "git reset --soft HEAD^ "
        executeInBash(command, path) |> ignore
        command <- "echo '" + corrected + "' | git hash-object -t commit -w --stdin "
        let hash = executeInBash(command, path) 
       
        command <- "git update-ref -m '" + message + "' refs/heads/" + branch + " " + hash
        let result = executeInBash(command, path) 
        ()



    let commitVersionToGit (
                                metadata_path: string, 
                                // current directory-version
                                dir_to_add: string, 
                                //git repostiroy
                                repo: Repository,
                                // commit  
                                message:string,
                                author_name:string, 
                                author_email:string, 
                                author_date:DateTimeOffset, 
                                committer_name:string, 
                                committer_email:string, 
                                commit_date,
                                // used only in the last version to commit out of version files 
                                ignore_files_to_commit: string seq,
                                // name of the branch that will contains the versions
                                branch_name,
                                // path inside master branch of versions. ie /source when versions are /source/v1 ...
                                relative_src_path,
                                // true if first commit
                                is_first_commit:bool,
                                // a tag name if the directory contains a release, empty otherwise
                                release_tag:string
                                ) =
        // split message on multiple lines by '|'
        let message =
            let message_lines = message.Split '|'
            let t = new System.Text.StringBuilder();
            for l in message_lines do t.Append(l) |> ignore; t.AppendLine() |> ignore done
            t.ToString()
        let mutable src_branch = repo.Branches.[branch_name]
        // move to branch containing versioning 
        Commands.Checkout(repo, src_branch) |> ignore
        let options = new CheckoutOptions()
        options.CheckoutModifiers <- CheckoutModifiers.Force 
        // get the current version from master to src branch
        repo.CheckoutPaths("master", ["source\\" + dir_to_add], options);
        // if there are out-of-version files
        if not(Seq.isEmpty ignore_files_to_commit)
            then
                for file in ignore_files_to_commit do
                    let file = file.Replace(repo.Info.WorkingDirectory + relative_src_path , "")
                    repo.CheckoutPaths("master", [relative_src_path + file], options)
                    if System.IO.File.Exists(repo.Info.WorkingDirectory + relative_src_path + file) 
                        then System.IO.File.Copy(repo.Info.WorkingDirectory + relative_src_path + file, repo.Info.WorkingDirectory + "/" + file )
                        else directoryCopy (repo.Info.WorkingDirectory + relative_src_path + file) (repo.Info.WorkingDirectory) true
                done
        let relative_src_path = "source\\"
        // move current version to the root of src branch        
        directoryCopy (repo.Info.WorkingDirectory + relative_src_path + dir_to_add) (repo.Info.WorkingDirectory) true
        let files_unstage =
                    Directory.GetFiles (repo.Info.WorkingDirectory + relative_src_path, "*.*", SearchOption.AllDirectories)
        System.IO.Directory.Delete(repo.Info.WorkingDirectory + relative_src_path, true)//cancella tutta source
        Commands.Unstage(repo, files_unstage)//unstage main.txt dentro source/v1
        // stage current version files
        let files =
                    let filter_metadata_files = fun (path: string) -> not(path = metadata_path)
                    Directory.GetFiles (repo.Info.WorkingDirectory, "*.*", SearchOption.AllDirectories)
                    |> Array.filter filter_git_files
                    |> Array.filter filter_metadata_files
        Commands.Stage(repo, files)//stage main dalla radice
        // Create the committer's signature
        let offset = author_date.ToUnixTimeSeconds()
        let author = Signature(author_name, author_email, author_date)
        let committer = Signature(committer_name, committer_email, commit_date)
        // Commit to the repository
        let mutable last_commit = List.head (Seq.toList repo.Commits)
        if is_first_commit
           then 
                let emptyCommit = Array.empty<Commit>
                let treeDefinition = new TreeDefinition()
                let tree = repo.ObjectDatabase.CreateTree(repo.Index)
                last_commit <- repo.ObjectDatabase.CreateCommit(author, committer, message, tree, emptyCommit, false)
                let master_branch =  repo.Branches.["master"]
                Commands.Checkout(repo, master_branch) |> ignore
                repo.Branches.Remove(src_branch)
                src_branch <- repo.Branches.Add(branch_name, last_commit)
                let force = new CheckoutOptions()
                force.CheckoutModifiers <- CheckoutModifiers.Force
                Commands.Checkout(repo, src_branch, force) |> ignore
            else
                last_commit <- repo.Commit(message, author, committer)
                () 
        if author_date.Year < 1970 
            then
                let bad_date = last_commit.Author.When 
                let path = repo.Info.WorkingDirectory
                fixPre1970Commit(author_date, bad_date, message, path, branch_name)
        //apply tag COMMENTATO PER ORA
        if not(String.IsNullOrWhiteSpace(release_tag)) 
                then 
                    //let repo.ApplyTag(release_tag)  //BUG: this do not annotate the tag
                    let command = "git tag -a " + dir_to_add + " -m \"" + release_tag + "\""
                    let path = repo.Info.WorkingDirectory
                    executeInBash(command, path) |> ignore
                    
        //clean up commited files
        Commands.Unstage(repo,files )
        Seq.iter (fun (file:FileInfo) ->  file.Delete()) (Seq.map (fun (file_path:string) -> new FileInfo(file_path)) files)  
        repo.Index.Clear()
        ()

    let after_latest_slash (dir: string) =
                        let temp =
                            if dir.Contains('\\') 
                                then dir.Split('\\')
                                else dir.Split('/')
                        temp.[temp.Length - 1]

    let getMetadata(metadata_path:string) = 
        CsvFile.Load(metadata_path).Rows |> Seq.toArray

    let createSyntheticGit (root_path: string, metadata_path: string, ignore_path: string) =
        let branch_name = "SourceCode"
        //if File.Exists("temp.csv")
        //    then    File.Delete("temp.csv")
        //File.Copy(metadata_path, "temp.csv")
        let authorsAndDateStrings = getMetadata(metadata_path)

        let version_directories = 
                    Seq.map (fun (ignore_row: CsvRow) -> ignore_row.Columns.[0]) authorsAndDateStrings
        let directories, not_versioned_files =
                let get_only_version_directories = fun (dir: string) ->
                                            (Seq.exists (fun (row: CsvRow) -> (
                                                let a = after_latest_slash dir 
                                                row.Columns.[0] = a)
                                                )
                                            authorsAndDateStrings)
                //TODO: better investigare how to handle symlink
                ///https://stackoverflow.com/questions/1485155/check-if-a-file-is-real-or-a-symbolic-link
                let filter_symbolic_links = fun (path: string) ->
                                    let pathInfo = new FileInfo(path);
                                    not(pathInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                in
                let a = Array.sort (Directory.GetDirectories(root_path))
                let b = a |> Array.filter filter_symbolic_links
                let c = b |> Array.partition get_only_version_directories
                c
        
        let not_versioned_files = Array.append not_versioned_files (Array.sort (Directory.GetFiles(root_path)))

        let last_dir = after_latest_slash (Array.last directories)
        let mutable is_first_commit = true
        let mutable git_path = ""
        let mutable relative_src_path  = ""
        let mutable git = new Repository();
        for dir in directories do
            let short_dir = after_latest_slash dir
            let info = 
                let finder = fun (row: CsvRow) -> (row.GetColumn row_directory_name = short_dir)
                Seq.tryFind finder (authorsAndDateStrings)

            let committer_name = if info.IsSome then info.Value.GetColumn row_curator_name else none (null)
            let committer_email = if info.IsSome then info.Value.GetColumn row_curator_email else none (null)

            let release_tag = if info.IsSome then info.Value.GetColumn row_release else none (null)

            if is_first_commit 
                then 
                    git <- initGit (root_path, branch_name, committer_name, committer_email)
                    //TODO: add check that git has clear status to avoid conflicts
                    git_path <- git.Info.Path
                    relative_src_path <- root_path.Replace(git_path.Replace("/.git", ""), "")

            let dest = git_path.Replace("/.git", "")
            let orig = Path.Combine(root_path, dir)

            let author_email = if info.IsSome then info.Value.GetColumn row_author_email else none (null)
            let author_name = if info.IsSome then info.Value.GetColumn row_author_name else none (null)

            let message =
                if info.IsSome then none (info.Value.GetColumn row_commit_message)
                else none (null)

            let author_date =
                let date_str = (info.Value.GetColumn row_date).Substring(0, 16)
                if info.IsSome then
                    DateTimeOffset.ParseExact
                        (date_str, date_format, System.Globalization.CultureInfo.InvariantCulture)
                else DateTimeOffset.Now

            let tag = if release_tag = "*" then short_dir else release_tag

            let files_to_commit_only_on_last_version =
                if short_dir  = last_dir
                    then Seq.ofArray not_versioned_files
                    else Seq.empty

            let commit_date = DateTimeOffset.Now
            Console.WriteLine
                ("Commit dir: {0} with message : {1} on {2}", orig, message, commit_date.ToLocalTime().ToString())
            commitVersionToGit
                (
                 metadata_path, 
                 short_dir,
                 git,
                 short_dir + " - " + message,
                 author_name.TrimEnd(),
                 author_email.TrimEnd(),
                 author_date,
                 committer_name.TrimEnd(),
                 committer_email.TrimEnd(),
                 commit_date,
                 files_to_commit_only_on_last_version,
                 branch_name,
                 relative_src_path,
                 is_first_commit,
                 tag
                    )
            is_first_commit <- false
            ()
        // restore directory to master 
        Commands.Checkout(git, "master") |> ignore 
        
        ()