// Copyright 2019 Maintainers of NUKE.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using JetBrains.Annotations;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Utilities.TemplateUtility;

namespace Nuke.GlobalTool
{
    partial class Program
    {
        // ReSharper disable InconsistentNaming

        public const string PLATFORM_NETCORE = "netcore";
        public const string PLATFORM_NETFX = "netfx";
        public const string FRAMEWORK_NET461 = "net461";
        public const string FRAMEWORK_NETCOREAPP2 = "netcoreapp2.0";
        public const string FORMAT_SDK = "sdk";
        public const string FORMAT_LEGACY = "legacy";

        // ReSharper restore InconsistentNaming

        [UsedImplicitly]
        private static int Setup(string[] args, [CanBeNull] string rootDirectory, [CanBeNull] string buildScript)
        {
            #region Basic

            var nukeLatestReleaseVersion = NuGetPackageResolver.GetLatestPackageVersion("Nuke.Common", includePrereleases: false);
            var nukeLatestPrereleaseVersion = NuGetPackageResolver.GetLatestPackageVersion("Nuke.Common", includePrereleases: true);
            var nukeLatestLocalVersion = NuGetPackageResolver.GetGlobalInstalledPackage("Nuke.Common", version: null, packagesConfigFile: null)
                ?.Version.ToString();

            if (rootDirectory == null)
            {
                var rootDirectoryItems = new[] { ".git", ".svn" };
                rootDirectory = FileSystemTasks.FindParentDirectory(
                    EnvironmentInfo.WorkingDirectory,
                    x => rootDirectoryItems.Any(y => x.GetFileSystemInfos(y, SearchOption.TopDirectoryOnly).Any()));
            }

            if (rootDirectory == null)
            {
                Logger.Warn("Could not find root directory. Falling back to working directory.");
                rootDirectory = EnvironmentInfo.WorkingDirectory;
            }

            var buildProjectName = ConsoleUtility.PromptForInput("How should the build project be named?", "_build");
            var buildDirectoryName = ConsoleUtility.PromptForInput("Where should the build project be located?", "./build");

            var targetPlatform = !EnvironmentInfo.GetParameter<bool>("boot")
                ? PLATFORM_NETCORE
                : ConsoleUtility.PromptForChoice("What bootstrapping method should be used?",
                    (PLATFORM_NETCORE, ".NET Core SDK"),
                    (PLATFORM_NETFX, ".NET Framework/Mono"));

            var targetFramework = targetPlatform == PLATFORM_NETFX
                ? FRAMEWORK_NET461
                : FRAMEWORK_NETCOREAPP2;

            var projectFormat = targetPlatform == PLATFORM_NETCORE
                ? FORMAT_SDK
                : ConsoleUtility.PromptForChoice("What project format should be used?",
                    (FORMAT_SDK, "SDK-based Format: requires .NET Core SDK"),
                    (FORMAT_LEGACY, "Legacy Format: supported by all MSBuild/Mono versions"));

            var nukeVersion = ConsoleUtility.PromptForChoice("Which NUKE version should be used?",
                new[]
                    {
                        ("latest release", nukeLatestReleaseVersion.GetAwaiter().GetResult()),
                        ("latest prerelease", nukeLatestPrereleaseVersion.GetAwaiter().GetResult()),
                        ("latest local", nukeLatestLocalVersion),
                        ("same as global tool", typeof(Program).GetTypeInfo().Assembly.GetVersionText())
                    }
                    .Where(x => x.Item2 != null)
                    .Distinct(x => x.Item2)
                    .Select(x => (x.Item2, $"{x.Item2} ({x.Item1})")).ToArray());

            var solutionFile = ConsoleUtility.PromptForChoice(
                "Which solution should be the default?",
                options: new DirectoryInfo(rootDirectory)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(x => x.FullName.EndsWithOrdinalIgnoreCase(".sln"))
                    .OrderByDescending(x => x.FullName)
                    .Select(x => (x, GetRelativePath(rootDirectory, x.FullName)))
                    .Concat((null, "None")).ToArray())?.FullName;
            var solutionDirectory = solutionFile != null ? Path.GetDirectoryName(solutionFile) : null;

            #endregion

            #region Additional

            var definitions = new List<string>();

            if (solutionFile != null &&
                projectFormat == FORMAT_SDK &&
                ConsoleUtility.PromptForChoice(
                    "Do you need help getting started with a basic build?",
                    (true, "Yes, get me started!"),
                    (false, "No, I can do this myself...")))
            {
                definitions.Add(
                    ConsoleUtility.PromptForChoice("Restore, compile, pack using ...",
                        ("DOTNET", "dotnet CLI"),
                        ("MSBUILD", "MSBuild/Mono"),
                        (null, "Neither")));

                definitions.Add(
                    ConsoleUtility.PromptForChoice("Source files are located in ...",
                        ("SOURCE_DIR", "./source"),
                        ("SRC_DIR", "./src"),
                        (null, "Neither")));

                definitions.Add(
                    ConsoleUtility.PromptForChoice("Move packages to ...",
                        ("OUTPUT_DIR", "./output"),
                        ("ARTIFACTS_DIR", "./artifacts"),
                        (null, "Neither")));

                definitions.Add(
                    ConsoleUtility.PromptForChoice("Where do test projects go?",
                        ("TESTS_DIR", "./tests"),
                        (null, "Same as source")));

                if (Directory.Exists(Path.Combine(rootDirectory, ".git")))
                    definitions.Add("GIT");
                else
                {
                    definitions.Add(
                        ConsoleUtility.PromptForChoice("Do you use git?",
                            ("GIT", "Yes, just not setup yet"),
                            (null, "No, something else")));
                }

                if (File.Exists(Path.Combine(rootDirectory, "GitVersion.yml")))
                    definitions.Add("GITVERSION");
                else if (definitions.Contains("GIT"))
                {
                    definitions.Add(
                        ConsoleUtility.PromptForChoice("Do you use GitVersion?",
                            ("GITVERSION", "Yes, just not setup yet"),
                            (null, "No, custom versioning")));
                }
            }

            definitions.RemoveAll(x => x == null);

            #endregion

            #region Generation

            var buildDirectory = Path.Combine(rootDirectory, buildDirectoryName);
            var buildProjectFile = Path.Combine(rootDirectory, buildDirectoryName, buildProjectName + ".csproj");
            var buildProjectGuid = Guid.NewGuid().ToString().ToUpper();
            var buildProjectKind = new Dictionary<string, string>
                                   {
                                       [FORMAT_LEGACY] = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC",
                                       [FORMAT_SDK] = "9A19103F-16F7-4668-BE54-9A1E7A4F7556"
                                   }[projectFormat];

            if (solutionFile == null)
            {
                FileSystemTasks.Touch(Path.Combine(rootDirectory, Constants.ConfigurationFileName));
            }
            else
            {
                TextTasks.WriteAllText(
                    Path.Combine(rootDirectory, Constants.ConfigurationFileName),
                    GetRelativePath(rootDirectory, solutionFile).Replace(oldChar: '\\', newChar: '/'));

                definitions.Add("SOLUTION_FILE");

                var solutionFileContent = TextTasks.ReadAllLines(solutionFile).ToList();
                var buildProjectFileRelative = (WinRelativePath) GetRelativePath(solutionDirectory, buildProjectFile);
                UpdateSolutionFileContent(solutionFileContent, buildProjectFileRelative, buildProjectGuid, buildProjectKind, buildProjectName);
                TextTasks.WriteAllLines(solutionFile, solutionFileContent, Encoding.UTF8);
            }

            TextTasks.WriteAllLines(
                buildProjectFile,
                FillTemplate(
                    GetTemplate($"_build.{projectFormat}.csproj"),
                    definitions,
                    replacements: GetDictionary(
                        new
                        {
                            solutionDirectory = (WinRelativePath) GetRelativePath(buildDirectory, solutionDirectory ?? rootDirectory),
                            rootDirectory = (WinRelativePath) GetRelativePath(buildDirectory, rootDirectory),
                            scriptDirectory = (WinRelativePath) GetRelativePath(buildDirectory, EnvironmentInfo.WorkingDirectory),
                            buildProjectName,
                            buildProjectGuid,
                            targetFramework,
                            nukeVersion,
                            nukeVersionMajorMinor = nukeVersion.Split(".").Take(2).Join(".")
                        })));

            if (projectFormat == FORMAT_LEGACY)
            {
                TextTasks.WriteAllLines(
                    Path.Combine(buildDirectory, "packages.config"),
                    FillTemplate(
                        GetTemplate("_build.legacy.packages.config"),
                        replacements: GetDictionary(new { nukeVersion })));
            }

            TextTasks.WriteAllLines(
                $"{buildProjectFile}.DotSettings",
                FillTemplate(
                    GetTemplate("_build.csproj.DotSettings")));

            TextTasks.WriteAllLines(
                Path.Combine(buildDirectory, ".editorconfig"),
                FillTemplate(
                    GetTemplate(".editorconfig")));

            TextTasks.WriteAllLines(
                Path.Combine(buildDirectory, "Build.cs"),
                FillTemplate(
                    GetTemplate("Build.cs"),
                    definitions));

            TextTasks.WriteAllLines(
                Path.Combine(EnvironmentInfo.WorkingDirectory, "build.ps1"),
                FillTemplate(
                    GetTemplate($"build.{targetPlatform}.ps1"),
                    replacements: GetDictionary(
                        new
                        {
                            rootDirectory = (WinRelativePath) GetRelativePath(EnvironmentInfo.WorkingDirectory, rootDirectory),
                            solutionDirectory =
                                (WinRelativePath) GetRelativePath(EnvironmentInfo.WorkingDirectory, solutionDirectory ?? rootDirectory),
                            scriptDirectory = (WinRelativePath) GetRelativePath(buildDirectory, EnvironmentInfo.WorkingDirectory),
                            buildDirectory = (WinRelativePath) GetRelativePath(EnvironmentInfo.WorkingDirectory, buildDirectory),
                            buildProjectName,
                            nugetVersion = "latest"
                        })));

            TextTasks.WriteAllLines(
                Path.Combine(EnvironmentInfo.WorkingDirectory, "build.sh"),
                FillTemplate(
                    GetTemplate($"build.{targetPlatform}.sh"),
                    replacements: GetDictionary(
                        new
                        {
                            rootDirectory = (UnixRelativePath) GetRelativePath(EnvironmentInfo.WorkingDirectory, rootDirectory),
                            solutionDirectory =
                                (UnixRelativePath) GetRelativePath(EnvironmentInfo.WorkingDirectory, solutionDirectory ?? rootDirectory),
                            scriptDirectory = (UnixRelativePath) GetRelativePath(buildDirectory, EnvironmentInfo.WorkingDirectory),
                            buildDirectory = (UnixRelativePath) GetRelativePath(EnvironmentInfo.WorkingDirectory, buildDirectory),
                            buildProjectName,
                            nugetVersion = "latest"
                        })));

            if (definitions.Contains("SRC_DIR"))
                FileSystemTasks.EnsureExistingDirectory(Path.Combine(rootDirectory, "src"));
            if (definitions.Contains("SOURCE_DIR"))
                FileSystemTasks.EnsureExistingDirectory(Path.Combine(rootDirectory, "source"));
            if (definitions.Contains("TESTS_DIR"))
                FileSystemTasks.EnsureExistingDirectory(Path.Combine(rootDirectory, "tests"));

            #endregion

            #region Wizard+Generation (addon)

            if (new[] { "addon", "addin", "plugin" }.Any(x => x.EqualsOrdinalIgnoreCase(args.FirstOrDefault())))
            {
                ControlFlow.Assert(definitions.Contains("SOURCE_DIR"), "definitions.Contains('SOURCE_DIR')");

                var organization = ConsoleUtility.PromptForInput("Organization name:", defaultValue: "nuke-build");
                var addonName = ConsoleUtility.PromptForInput("Organization name:", defaultValue: null);
                var authors = ConsoleUtility.PromptForInput("Author names separated by comma:", defaultValue: "Matthias Koch, Sebastian Karasek");
                var packageName = ConsoleUtility.PromptForInput("Package name on nuget.org:", defaultValue: null);

                TextTasks.WriteAllLines(
                    Path.Combine(rootDirectory, "README.md"),
                    FillTemplate(
                        GetTemplate("README.md"),
                        replacements: GetDictionary(
                            new
                            {
                                organization,
                                addonName,
                                authors,
                                packageName
                            })));

                TextTasks.WriteAllLines(
                    Path.Combine(rootDirectory, "LICENSE"),
                    FillTemplate(
                        GetTemplate("LICENSE"),
                        replacements: GetDictionary(
                            new
                            {
                                year = DateTime.Now.Year,
                                authors
                            })));

                TextTasks.WriteAllLines(
                    Path.Combine(rootDirectory, "CHANGELOG.md"),
                    FillTemplate(
                        GetTemplate("CHANGELOG.md")));

                TextTasks.WriteAllText(
                    $"{solutionFile}.DotSettings.ext",
                    "https://raw.githubusercontent.com/nuke-build/common/develop/nuke-common.sln.DotSettings");
                TextTasks.WriteAllText(
                    Path.Combine(solutionDirectory, "source", "Inspections.DotSettings.ext"),
                    "https://raw.githubusercontent.com/nuke-build/common/develop/source/Inspections.DotSettings");
                TextTasks.WriteAllText(
                    Path.Combine(solutionDirectory, "source", "CodeStyle.DotSettings.ext"),
                    "https://raw.githubusercontent.com/nuke-build/common/develop/source/CodeStyle.DotSettings");
                TextTasks.WriteAllText(
                    Path.Combine(solutionDirectory, "source", "Configuration.props.ext"),
                    "https://raw.githubusercontent.com/nuke-build/common/develop/source/Configuration.props");
            }

            #endregion

            return 0;
        }

        public static void UpdateSolutionFileContent(
            List<string> content,
            string buildProjectFileRelative,
            string buildProjectGuid,
            string buildProjectKind,
            string buildProjectName)
        {
            if (content.Any(x => x.Contains(buildProjectFileRelative)))
                return;

            var globalIndex = content.IndexOf("Global");
            ControlFlow.Assert(globalIndex != -1, "Could not find a 'Global' section in solution file.");

            var projectConfigurationIndex = content.FindIndex(x => x.Contains("GlobalSection(ProjectConfigurationPlatforms)"));
            if (projectConfigurationIndex == -1)
            {
                var solutionConfigurationIndex = content.FindIndex(x => x.Contains("GlobalSection(SolutionConfigurationPlatforms)"));
                if (solutionConfigurationIndex == -1)
                {
                    content.Insert(globalIndex + 1, "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
                    content.Insert(globalIndex + 2, "\t\tDebug|Any CPU = Debug|Any CPU");
                    content.Insert(globalIndex + 3, "\t\tRelease|Any CPU = Release|Any CPU");
                    content.Insert(globalIndex + 4, "\tEndGlobalSection");

                    solutionConfigurationIndex = globalIndex + 1;
                }

                var endGlobalSectionIndex = content.FindIndex(solutionConfigurationIndex, x => x.Contains("EndGlobalSection"));

                content.Insert(endGlobalSectionIndex + 1, "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
                content.Insert(endGlobalSectionIndex + 2, "\tEndGlobalSection");

                projectConfigurationIndex = endGlobalSectionIndex + 1;
            }

            content.Insert(projectConfigurationIndex + 1, $"\t\t{{{buildProjectGuid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            content.Insert(projectConfigurationIndex + 2, $"\t\t{{{buildProjectGuid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU");

            content.Insert(globalIndex,
                $"Project(\"{{{buildProjectKind}}}\") = \"{buildProjectName}\", \"{buildProjectFileRelative}\", \"{{{buildProjectGuid}}}\"");
            content.Insert(globalIndex + 1,
                "EndProject");
        }

        private static string[] GetTemplate(string templateName)
        {
            return ResourceUtility.GetResourceAllLines<Program>($"templates.{templateName}");
        }
    }
}
