// Copyright 2019 Maintainers of NUKE.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Linq;
using Nuke.Common.Utilities;

namespace Nuke.Common.CI.TeamCity.Configuration
{
    public static class TeamCityCustomWriterExtensions
    {
        public static IDisposable WriteBlock(this CustomFileWriter writer, string text)
        {
            return DelegateDisposable
                .CreateBracket(
                    () => writer.WriteLine(string.IsNullOrWhiteSpace(text)
                        ? "{"
                        : $"{text} {{"),
                    () => writer.WriteLine("}"))
                .CombineWith(writer.Indent());
        }
    }
}
