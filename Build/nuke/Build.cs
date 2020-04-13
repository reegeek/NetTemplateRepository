using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.AppVeyor;
using Nuke.Common.CI.AzurePipelines;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
[GitHubActions(
    "continuous",
    GitHubActionsImage.WindowsServer2016R2,
    GitHubActionsImage.WindowsServer2019,
    On = new[] { GitHubActionsTrigger.Push },
    ImportGitHubTokenAs = nameof(GitHubToken),
    InvokedTargets = new[] { nameof(Test), nameof(Pack) })]
[GitHubActions(
    "continuousCore",
    GitHubActionsImage.Ubuntu1804,
    GitHubActionsImage.MacOsLatest,
    On = new[] { GitHubActionsTrigger.Push },
    ImportGitHubTokenAs = nameof(GitHubToken),
    InvokedTargets = new[] { nameof(TestOnlyCore) })]
[AppVeyor(
    AppVeyorImage.VisualStudio2019,
    SkipTags = true,
    InvokedTargets = new[] { nameof(Test), nameof(Pack) })]
[AzurePipelines(
    suffix: null,
    AzurePipelinesImage.WindowsLatest,
    InvokedTargets = new[] { nameof(Test), nameof(Pack) },
    NonEntryTargets = new[] { nameof(Restore) },
    ExcludedTargets = new[] { nameof(Clean)})]

partial class Build : Nuke.Common.NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion] readonly GitVersion GitVersion;

    [CI] readonly AzurePipelines AzurePipelines;
    [Parameter("GitHub Token")] readonly string GitHubToken;


    string ChangelogFile => RootDirectory / "CHANGELOG.md";

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath ResultDirectory => RootDirectory / ".result";
    AbsolutePath PackagesDirectory => ResultDirectory / "packages";
    AbsolutePath TestResultDirectory => ResultDirectory / "test-results";

    Project TemplateProject => Solution.GetProject("Template");
    IEnumerable<Project> TestProjects => Solution.GetProjects("*.Tests");

    IEnumerable<Project> AllProjects => Solution.AllProjects.Where(x=> SourceDirectory.Contains(x.Path));

    bool ExcludeNetFramework { get; set; } = false;

    string[] Frameworks { get; set; }

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            EnsureCleanDirectory(ResultDirectory);
        });

    Target ExcludeNetFrameworkTarget => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            ExcludeNetFramework = true;
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            Logger.Info(ExcludeNetFramework ? "Exclude net framework" : "Include net framework");

            if (ExcludeNetFramework)
            {
                var frameworks =
                    from project in AllProjects
                    from framework in project.GetTargetFrameworks(ExcludeNetFramework)
                    select new {project, framework};


                DotNetBuild(s => s
                    .SetConfiguration(Configuration)
                    .EnableNoRestore()
                    .SetAssemblyVersion(GitVersion.AssemblySemVer)
                    .SetFileVersion(GitVersion.AssemblySemFileVer)
                    .SetInformationalVersion(GitVersion.InformationalVersion)
                    .CombineWith(frameworks, (s, f) => s
                        .SetFramework(f.framework)
                        .SetProjectFile(f.project)));
            }
            else
            {
                DotNetBuild(s => s
                    .SetConfiguration(Configuration)
                    .EnableNoRestore()
                    .SetAssemblyVersion(GitVersion.AssemblySemVer)
                    .SetFileVersion(GitVersion.AssemblySemFileVer)
                    .SetInformationalVersion(GitVersion.InformationalVersion));
            }
        });

    Target Publish => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var publishConfigurations =
                from project in new[] { TemplateProject }
                from framework in project.GetTargetFrameworks(ExcludeNetFramework)
                select new { project, framework };

            DotNetPublish(_ => _
                    .EnableNoRestore()
                    .EnableNoBuild()
                    .SetConfiguration(Configuration)
                    .SetAssemblyVersion(GitVersion.AssemblySemVer)
                    .SetFileVersion(GitVersion.AssemblySemFileVer)
                    .SetInformationalVersion(GitVersion.InformationalVersion)
                    .CombineWith(publishConfigurations, (_, v) => _
                        .SetProject(v.project)
                        .SetFramework(v.framework)),
                10);

        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Produces(TestResultDirectory / "*.trx")
        .Executes(() =>
        {
            var testConfigurations =
                from project in TestProjects
                from framework in project.GetTargetFrameworks(ExcludeNetFramework)
                select new { project, framework };


            DotNetTest(_ => _
                .SetConfiguration(Configuration)
                .EnableNoRestore()
                .EnableNoBuild()
                .ResetVerbosity()
                .SetResultsDirectory(TestResultDirectory)
                .CombineWith(testConfigurations, (_, v) => _
                    .SetProjectFile(v.project)
                    .SetFramework(v.framework)
                    .SetLogger($"trx;LogFileName={v.project.Name}.trx")),
                10);

            TestResultDirectory.GlobFiles("*.trx").ForEach(x =>
                AzurePipelines?.PublishTestResults(
                    type: AzurePipelinesTestResultsType.VSTest,
                    title: $"{Path.GetFileNameWithoutExtension(x)} ({AzurePipelines.StageDisplayName})",
                    files: new string[] { x }));
        });

    Target TestOnlyCore => _ => _
        .DependsOn(Compile, ExcludeNetFrameworkTarget)
        .Produces(TestResultDirectory / "*.trx")
        .Executes(() =>
        {
            var testConfigurations =
                from project in TestProjects
                from framework in project.GetTargetFrameworks(ExcludeNetFramework)
                select new { project, framework };


            DotNetTest(_ => _
                    .SetConfiguration(Configuration)
                    .EnableNoRestore()
                    .EnableNoBuild()
                    .ResetVerbosity()
                    .SetResultsDirectory(TestResultDirectory)
                    .CombineWith(testConfigurations, (_, v) => _
                        .SetProjectFile(v.project)
                        .SetFramework(v.framework)
                        .SetLogger($"trx;LogFileName={v.project.Name}.trx")),
                10);

            TestResultDirectory.GlobFiles("*.trx").ForEach(x =>
                AzurePipelines?.PublishTestResults(
                    type: AzurePipelinesTestResultsType.VSTest,
                    title: $"{Path.GetFileNameWithoutExtension(x)} ({AzurePipelines.StageDisplayName})",
                    files: new string[] { x }));
        });


    Target Pack => _ => _
        .DependsOn(Publish)
        .Produces(PackagesDirectory / "*.nupkg")
        .Executes(() =>
        {
            DotNetPack(_ => _
                .SetProject(Solution)
                .EnableNoRestore()
                .EnableNoBuild()
                .SetConfiguration(Configuration)
                .SetOutputDirectory(PackagesDirectory)
                .SetVersion(GitVersion.NuGetVersionV2));
        });

}