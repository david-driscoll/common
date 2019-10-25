﻿// Copyright 2019 Maintainers of NUKE.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Nuke.Common.Execution;

namespace Nuke.Common
{
    /// <summary>
    ///     Injected parameters are resolved case-insensitively in the following order:
    ///     <ul>
    ///         <li>From command-line arguments (e.g., <c>-arg value</c>)</li>
    ///         <li>From environment variables (e.g., <c>Arg=value</c>)</li>
    ///     </ul>
    ///     <para/>
    ///     For value-types, there is a distinction between pure value-types, and their <em>nullable</em>
    ///     counterparts. For instance, <c>int</c> will have its default value <c>0</c> even when it's not
    ///     supplied via command-line or environment variable.
    ///     <para/>
    /// </summary>
    /// <example>
    ///     <code>
    /// [Parameter("Configuration to build")] readonly Configuration Configuration;
    /// [Parameter("API key for NuGet")] readonly string ApiKey;
    /// [Parameter("Custom items")] readonly string[] Items;
    ///     </code>
    /// </example>
    [PublicAPI]
    public class ParameterAttribute : InjectionAttributeBase
    {
        public ParameterAttribute(string description = null)
        {
            Description = description;
        }

        public virtual string Description { get; }

        [CanBeNull]
        public string Name { get; set; }

        [CanBeNull]
        public string Separator { get; set; }

        public virtual bool List { get; set; } = true;

        [CanBeNull]
        public string ValueProvider { get; set; }

        public override bool IsFast => true;

        [CanBeNull]
        public override object GetValue(MemberInfo member, object instance)
        {
            return EnvironmentInfo.GetParameter<object>(member, member.GetMemberType().GetNullableType());
        }

        public virtual IEnumerable<(string, object)> GetValueSet(MemberInfo member, object instance)
        {
            return null;
        }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class RequiredAttribute : Attribute
    {
    }
}
