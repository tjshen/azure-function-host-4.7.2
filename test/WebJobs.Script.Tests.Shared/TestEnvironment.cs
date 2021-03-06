// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestEnvironment : IEnvironment
    {
        private readonly IDictionary<string, string> _variables;

        public TestEnvironment()
            : this(new Dictionary<string, string>())
        {
        }

        public TestEnvironment(IDictionary<string, string> variables)
        {
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
        }

        public void Clear()
        {
            _variables.Clear();
        }

        public string GetEnvironmentVariable(string name)
        {
            _variables.TryGetValue(name, out string result);

            return result;
        }

        public virtual void SetEnvironmentVariable(string name, string value)
        {
            if (value == null && _variables.ContainsKey(name))
            {
                _variables.Remove(name);
                return;
            }

            _variables[name] = value;
        }

        public static TestEnvironment FromEnvironmentVariables()
        {
            var variables = Environment.GetEnvironmentVariables()
                .Cast<DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string)e.Value, StringComparer.OrdinalIgnoreCase);

            return new TestEnvironment(variables);
        }
    }
}
