///////////////////////////////////////////////////////////////////////////////
// TOOLS / ADDINS
///////////////////////////////////////////////////////////////////////////////

#tool paket:?package=GitVersion.CommandLine
#tool paket:?package=gitreleasemanager
#tool paket:?package=vswhere
#addin paket:?package=Cake.Git
#addin paket:?package=Cake.Figlet
#addin paket:?package=Cake.Paket

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
if (string.IsNullOrWhiteSpace(target))
{
    target = "Default";
}

var configuration = Argument("configuration", "Release");
if (string.IsNullOrWhiteSpace(configuration))
{
    configuration = "Release";
}

///////////////////////////////////////////////////////////////////////////////
// PREPARATION
///////////////////////////////////////////////////////////////////////////////

// Set build version
GitVersion(new GitVersionSettings { OutputType = GitVersionOutput.BuildServer });
GitVersion gitVersion = GitVersion(new GitVersionSettings { OutputType = GitVersionOutput.Json });

var latestInstallationPath = VSWhereProducts("*", new VSWhereProductSettings { Version = "[\"15.0\",\"16.0\"]" }).FirstOrDefault();
var msBuildPath = latestInstallationPath.CombineWithFilePath("./MSBuild/15.0/Bin/MSBuild.exe");

var local = BuildSystem.IsLocalBuild;
var isPullRequest = AppVeyor.Environment.PullRequest.IsPullRequest;
var branchName = GitBranchCurrent("../").FriendlyName;
var isDevelopBranch = StringComparer.OrdinalIgnoreCase.Equals("develop", branchName);
var isReleaseBranch = StringComparer.OrdinalIgnoreCase.Equals("master", branchName);
var isTagged = AppVeyor.Environment.Repository.Tag.IsTag;

// Directories and Paths
var solution = "Awesome.sln";
var publishDir = "./Publish";

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(ctx =>
{
    // Executed BEFORE the first task.

    if (!IsRunningOnWindows())
    {
        throw new NotImplementedException("Awesome will only build on Windows because it's not possible to target WPF and Windows Forms from UNIX.");
    }

    Information(Figlet("Awesome"));

    Information("Informational   Version: {0}", gitVersion.InformationalVersion);
    Information("SemVer          Version: {0}", gitVersion.SemVer);
    Information("AssemblySemVer  Version: {0}", gitVersion.AssemblySemVer);
    Information("MajorMinorPatch Version: {0}", gitVersion.MajorMinorPatch);
    Information("NuGet           Version: {0}", gitVersion.NuGetVersion);
    Information("IsLocalBuild           : {0}", local);
    Information("Branch                 : {0}", branchName);
    Information("Configuration          : {0}", configuration);
});

Teardown(ctx =>
{
   // Executed AFTER the last task.
});

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
    //.ContinueOnError()
    .Does(() =>
{
    var directoriesToDelete = GetDirectories("./**/obj").Concat(GetDirectories("./**/bin")).Concat(GetDirectories("./**/Publish"));
    DeleteDirectories(directoriesToDelete, new DeleteDirectorySettings { Recursive = true, Force = true });
});

Task("NuGet-Paket-Restore")
    .IsDependentOn("Clean")
    .Does(() =>
{
    PaketRestore();

    var msBuildSettings = new MSBuildSettings() { ToolPath = msBuildPath };
    MSBuild(solution, msBuildSettings.SetVerbosity(Verbosity.Normal).WithTarget("restore"));
});

Task("Update-SolutionInfo")
    .Does(() =>
{
	GitVersion(new GitVersionSettings { UpdateAssemblyInfo = true, UpdateAssemblyInfoFilePath = "./AwesomeControls/Properties/AssemblyInfo.cs"});
	GitVersion(new GitVersionSettings { UpdateAssemblyInfo = true, UpdateAssemblyInfoFilePath = "./Awesome/Properties/AssemblyInfo.cs"});
});

Task("Build")
    .IsDependentOn("NuGet-Paket-Restore")
    .Does(() =>
{
  var msBuildSettings = new MSBuildSettings() { ToolPath = msBuildPath, ArgumentCustomization = args => args.Append("/m") };
  MSBuild(solution, msBuildSettings.SetMaxCpuCount(0)
                                   .SetVerbosity(Verbosity.Normal)
                                   //.WithRestore() only with cake 0.28.x
                                   .SetConfiguration(configuration)
                                   );
});

Task("Zip-Demos")
    .WithCriteria(() => !isPullRequest)
    .Does(() =>
{
    EnsureDirectoryExists(Directory(publishDir));
    Zip("./Awesome/bin/" + configuration, publishDir + "/Awesome-v" + gitVersion.NuGetVersion + ".zip");
});


Task("CreateRelease")
    .WithCriteria(() => !isTagged)
    .WithCriteria(() => !isPullRequest)
    .Does(() =>
{
    var username = EnvironmentVariable("GITHUB_USERNAME");
    if (string.IsNullOrEmpty(username))
    {
        throw new Exception("The GITHUB_USERNAME environment variable is not defined.");
    }

    var token = EnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        throw new Exception("The GITHUB_TOKEN environment variable is not defined.");
    }

    GitReleaseManagerCreate(username, token, "punker76", "gitversiontest2", new GitReleaseManagerCreateSettings {
        Milestone         = gitVersion.MajorMinorPatch,
        Name              = gitVersion.SemVer,
        Prerelease        = false,
        TargetCommitish   = branchName,
        WorkingDirectory  = "../"
    });
});

Task("CreatePreRelease")
    .WithCriteria(() => !isTagged)
    .WithCriteria(() => !isPullRequest)
    .Does(() =>
{
    var username = EnvironmentVariable("GITHUB_USERNAME");
    if (string.IsNullOrEmpty(username))
    {
        throw new Exception("The GITHUB_USERNAME environment variable is not defined.");
    }

    var token = EnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        throw new Exception("The GITHUB_TOKEN environment variable is not defined.");
    }

    GitReleaseManagerCreate(username, token, "punker76", "gitversiontest2", new GitReleaseManagerCreateSettings {
        Milestone         = gitVersion.MajorMinorPatch,
        Name              = gitVersion.SemVer,
        Prerelease        = true,
        TargetCommitish   = branchName,
        WorkingDirectory  = "../"
    });
});

Task("ExportReleaseNotes")
    .Does(() =>
{
    var username = EnvironmentVariable("GITHUB_USERNAME");
    if (string.IsNullOrEmpty(username))
    {
        throw new Exception("The GITHUB_USERNAME environment variable is not defined.");
    }

    var token = EnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        throw new Exception("The GITHUB_TOKEN environment variable is not defined.");
    }

    EnsureDirectoryExists(Directory(publishDir));
    GitReleaseManagerExport(username, token, "punker76", "gitversiontest2", publishDir + "/releasenotes.md", new GitReleaseManagerExportSettings {
        TagName         = gitVersion.MajorMinorPatch,
        TargetDirectory = publishDir,
        LogFilePath     = publishDir + "/grm.log"
    });
});

///////////////////////////////////////////////////////////////////////////////
// TASK TARGETS
///////////////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Update-SolutionInfo")
    .IsDependentOn("Build");

Task("appveyor")
    .IsDependentOn("Update-SolutionInfo")
    .IsDependentOn("Build")
    .IsDependentOn("Zip-Demos");

///////////////////////////////////////////////////////////////////////////////
// EXECUTION
///////////////////////////////////////////////////////////////////////////////

RunTarget(target);