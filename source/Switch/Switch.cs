//[assembly: System.Reflection.AssemblyVersion("3.5.2.114")]
//[assembly: System.Reflection.AssemblyFileVersion("3.5.2.114")]

namespace utility
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    public static class IDictionaryExtension
    {
        public static bool ContainsConflatedKey<V>(this IDictionary<string, V> dictionary, string keys)
        {
            string[] names = keys.Split('|');
            foreach (string name in names)
                if (dictionary.ContainsKey(name))
                    return true;
            return false;
        }

        public static V GetConflatedValue<V>(this IDictionary<string, V> dictionary, string keys)
        {
            string[] names = keys.Split('|');
            foreach (string name in names)
                if (dictionary.ContainsKey(name))
                    return dictionary[name];
            return default(V);
        }
    }

    #region Switch - design strategy v2.0
    public class Switch : IEnumerable<string>
    {
        public const string regexpr = @"(?<switch>^[-/]+)(?<name>[\w\.?]+)((?<turn>[+-])|[:=](?<value>.*))?";

        private const string ON = "+";
        private const string OFF = "-";

        private string[] args;
        private Dictionary<int, Dictionary<string, NamedArgument>> namespaces;
        private List<PositionedArgument> positioned;
        private List<Input> AllArgument;
        private System.Collections.ObjectModel.ReadOnlyCollection<string> readonly_values;
        private System.Collections.ObjectModel.ReadOnlyCollection<KeyValuePair<string, string>> readonly_switches;
        private Dictionary<Type, utility.Switch.InstanceCreator> creators;
        private IResponseProvider ResponseProvider;

        public interface IResponseEnumerable : IDisposable, IEnumerable<string> { }

        public interface IResponseProvider
        {
            IResponseEnumerable Open(string resource_name);
        }

        public delegate object InstanceCreator(string input);

        internal class Input
        {
            public int AbsoluteIndex;
        }

        internal class Option
        {
            public Option(string name, bool matched, string value) { Name = name; Matched = matched; Value = value; }
            public string Name;
            public string Value;
            public bool Matched;
        }

        internal class NamedArgument : Input
        {
            public NamedArgument(Option name, Option turn, Option value) { Name = name; Turn = turn; Value = value; }
            public NamedArgument(Group name, Group turn, Group value)
            {
                Name = new Option("name", name.Success, name.Value);
                Turn = new Option("turn", turn.Success, turn.Value);
                Value = new Option("value", value.Success, value.Value);
            }
            public Option Name;
            public Option Turn;
            public Option Value;
        }

        internal class PositionedArgument : Input
        {
            public string Value;
        }

        internal class DefaultResponseProvider : IResponseProvider, IResponseEnumerable, IEnumerator<string>
        {
            public DefaultResponseProvider()
            {
                reader = null;
                current_argument = null;
            }

            IResponseEnumerable IResponseProvider.Open(string resource_name)
            {
                if (!System.IO.File.Exists(resource_name))
                    throw new ArgumentException("Response file not found:", resource_name);
                return new DefaultResponseProvider(resource_name);
            }

            private System.IO.StreamReader reader;
            private string current_argument;

            public DefaultResponseProvider(string resource_name)
            {
                reader = System.IO.File.OpenText(resource_name);
                current_argument = null;
            }

            public void Dispose()
            {
                if (reader != null) reader.Dispose();
            }

            #region IEnumerable<string> Members
            IEnumerator<string> IEnumerable<string>.GetEnumerator()
            {
                return this;
            }
            #endregion
            #region IEnumerable Members
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this;
            }
            #endregion

            #region IEnumerator<string> Members
            string IEnumerator<string>.Current
            {
                get { return current_argument; }
            }
            #endregion

            #region IEnumerator Members

            object System.Collections.IEnumerator.Current
            {
                get { return current_argument; }
            }

            bool System.Collections.IEnumerator.MoveNext()
            {
                current_argument = reader.ReadLine();
                return (current_argument != null);
            }

            void System.Collections.IEnumerator.Reset()
            {
                throw new NotImplementedException();
            }
            #endregion
        }

        public Switch(string[] opts) : this(opts, StringComparer.CurrentCultureIgnoreCase, null, null) { }
        public Switch(string[] opts, StringComparer comparer) : this(opts, comparer, null, null) { }
        public Switch(string[] opts, Dictionary<Type, utility.Switch.InstanceCreator> creators) : this(opts, StringComparer.CurrentCultureIgnoreCase, creators, null) { }
        public Switch(string[] opts, IResponseProvider response_provider) : this(opts, StringComparer.CurrentCultureIgnoreCase, null, response_provider) { }
        public Switch(string[] opts, StringComparer comparer, Dictionary<Type, utility.Switch.InstanceCreator> creators, IResponseProvider response_provider)
        {
            this.creators = creators;
            int length = opts == null ? 0 : opts.Length;
            this.args = new string[length];
            if (opts != null) Array.Copy(opts, args, length);
            this.namespaces = new Dictionary<int, Dictionary<string, NamedArgument>>();
            this.positioned = new List<PositionedArgument>();
            this.AllArgument = new List<Input>();
            this.ResponseProvider = response_provider == null ? new DefaultResponseProvider() : response_provider;
            var expr = new System.Text.RegularExpressions.Regex(regexpr, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            int absolute = 0; //to index count
            foreach (string arg in GetIndividualArguments(args))
            {
                Match match = expr.Match(arg);
                Group swit = match.Groups["switch"];
                Group name = match.Groups["name"];
                Input entry;
                if (swit.Success && name.Success)
                {
                    int namespace_id = swit.Length;
                    if (!this.namespaces.ContainsKey(namespace_id))
                        this.namespaces[namespace_id] = new Dictionary<string, NamedArgument>(comparer);
                    var named = this.namespaces[namespace_id];
                    named[name.Value] = new NamedArgument(name, match.Groups["turn"], match.Groups["value"]) { AbsoluteIndex = absolute };
                    entry = named[name.Value];
                }
                else
                {
                    entry = new PositionedArgument() { Value = arg, AbsoluteIndex = absolute };
                    this.positioned.Add(entry as PositionedArgument);
                }
                this.AllArgument.Add(entry);
                ++absolute;
            }
        }

        //get all lines of related file and process them as a single command-line fragment
        //other mode: process the file to deserialize a ?specified? class type
        //response file contains 1 argument per line (free of shell quote processing)
        private IEnumerable<string> GetArgsInResponse(string response_file)
        {
            using (var response = ResponseProvider.Open(response_file))
                foreach (string argument in response)
                    foreach (string inlinearg in GetIndividualArguments(argument))
                        yield return inlinearg;
        }

        private IEnumerable<string> GetIndividualArguments(params string[] args)
        {
            foreach (string arg in args)
                if (arg.StartsWith("@") && arg.Length > 1)
                    foreach (string argument in GetArgsInResponse(arg.Substring(1)))
                        yield return argument;
                else if (string.IsNullOrWhiteSpace(arg))
                    continue;
                else
                    yield return arg;
        }

        public static object AsType(string[] args, Type schema)
        {
            object target = Activator.CreateInstance(schema);
            return AsType(args, target);
        }
        public static SchemaType AsType<SchemaType>(string[] args) where SchemaType : new()
        {
            return AsType<SchemaType>(new Switch(args));
        }
        public static SchemaType AsType<SchemaType>(string[] args, SchemaType target)
        {
            return AsType(new Switch(args), target);
        }
        public static SchemaType AsType<SchemaType>(Switch opts) where SchemaType : new()
        {
            return AsType(opts, new SchemaType());
        }

        public SchemaType AsType<SchemaType>() where SchemaType : new()
        {
            return AsType(this, new SchemaType());
        }

        //de-facto deprecated?
        //public SchemaType AsType<SchemaType>(SchemaType target)
        //{
        //    return AsType(this, target);
        //}

        public static SchemaType AsType<SchemaType>(Switch opts, SchemaType target)
        {
            Type schema = target.GetType();
            System.Reflection.BindingFlags filter = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase;
            System.Reflection.MemberTypes gauge = System.Reflection.MemberTypes.Property | System.Reflection.MemberTypes.Field;
            foreach (KeyValuePair<string, string> named in opts.NamedArguments)
            {
                System.Reflection.MemberInfo[] member = schema.GetMember(named.Key, gauge, filter);
                if (member.Length == 0) continue;
                Type member_type = member[0].MemberType == System.Reflection.MemberTypes.Field ? (member[0] as System.Reflection.FieldInfo).FieldType : (member[0] as System.Reflection.PropertyInfo).PropertyType;
                string member_name = member[0].Name;
                ParseAndAssignValue(schema, target, opts, member_type, member_name, 1);
            }
            gauge = System.Reflection.MemberTypes.Method;
            foreach (KeyValuePair<string, string> named in opts.NamedArguments)
            {
                System.Reflection.MemberInfo[] member = schema.GetMember(named.Key, gauge, filter);
                if (member.Length == 0) continue;
                schema.InvokeMember(member[0].Name, System.Reflection.BindingFlags.InvokeMethod, null, target, null);
            }
            return target;
        }

        public TargetType AsType<TargetType>(int index)
        {
            Function<Switch, int, IList<string>> itemprovider = (args, arg_index) => new List<string>(SystemArgumentParser.Parse(args[arg_index]));
            return (TargetType)ParseTypeValue(this, this[index], typeof(TargetType), index, default(TargetType), itemprovider, 1);
        }

        public TargetType AsType<TargetType>(string opt_name, int namespace_id = 1)
        {
            return (TargetType)ParseTypeMemberValue(this, typeof(TargetType), opt_name, default(TargetType), namespace_id);
        }

        private static void ParseAndAssignValue(Type schema, object target, Switch opts, Type member_type, string member_name, int namespace_id)
        {
            object default_value = schema.InvokeMember(member_name, System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.GetField, null, target, null);
            object value = ParseTypeMemberValue(opts, member_type, member_name, default_value, namespace_id);
            schema.InvokeMember(member_name, System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.SetField, null, target, new object[] { value });
        }

        internal delegate TResult Function<TArgs, TNameOrIndex, TResult>(TArgs opts, TNameOrIndex member_name_or_index);

        private static object ParseTypeMemberValue(Switch opts, Type member_type, string member_name, object default_value, int namespace_id)
        {
            if (member_type == typeof(bool))
                return opts.Is(member_name, namespace_id);
            if (member_type.IsGenericType && member_type.GetGenericTypeDefinition() == typeof(Nullable<>))
                return ParseNullableTypeValue(opts, member_type.GetGenericArguments()[0], member_name, namespace_id);
            Function<Switch, string, IList<string>> itemprovider = (args, name) => new List<string>(args.SuffixOf(name, namespace_id));
            return ParseTypeValue(opts, opts[member_name], member_type, member_name, default_value, itemprovider, namespace_id);
        }

        private static object ParseNullableTypeValue(Switch opts, Type member_type, string member_name, int namespace_id)
        {
            if (opts.namespaces.ContainsKey(namespace_id) && opts.namespaces[namespace_id].Keys.Any(k => string.Compare(k, member_name, true) == 0))
            {
                object default_value = Activator.CreateInstance(member_type);
                return ParseTypeMemberValue(opts, member_type, member_name, default_value, namespace_id);
            }
            else
            {
                return null;
            }
        }

        private static object ParseTypeValue<T>(Switch opts, string typevalue, Type member_type, T member_name_or_index, object default_value, Function<Switch, T, IList<string>> itemsprovider, int namespace_id)
        {
            if (opts.creators != null && opts.creators.ContainsKey(member_type))
                return opts.creators[member_type](typevalue);
            if (member_type.IsArray)
            {
                Type element_type = member_type.GetElementType();
                if (IsLiteralValue(element_type))
                    return ParseArrayOfLiterals(itemsprovider(opts, member_name_or_index), element_type, member_name_or_index, null);
                else
                    return ParseArrayOfObjects(typevalue, element_type);
            }
            if (IsLiteralValue(member_type))
                return ParseTypeLiteralValue(member_type, typevalue, default_value);
            if (IsDictionary(member_type))
                return ParseDictionary(opts, member_type, member_name_or_index, default_value, namespace_id);
            if (IsGenericSingleTypeParameterCollection(member_type))
            {
                Type element_type = member_type.GetGenericArguments()[0];
                if (IsLiteralValue(element_type))
                    return ParseGenericCollectionOfLiterals(itemsprovider(opts, member_name_or_index), member_type, element_type, null);
                else
                    return ParseGenericCollectionOfObjects(typevalue, member_type, element_type);
            }
            if (member_type.IsClass)
            {
                string[] subargs = SystemArgumentParser.Parse(typevalue);
                return Switch.AsType(subargs, member_type);
            }
            throw new Exception(string.Format("The data type for {0} is not supported yet:{1}", member_name_or_index, member_type.FullName));
        }

        private static bool IsLiteralValue(Type type)
        {
            return type.IsEnum || type.IsPrimitive || type.IsValueType || type == typeof(string) || HasSingleStringConstructor(type) || HasStaticStringParseMethod(type);
        }

        private static bool HasSingleStringConstructor(Type type)
        {
            return type.GetConstructor(new Type[] { typeof(string) }) != null;
        }

        private static bool HasStaticStringParseMethod(Type type)
        {
            System.Reflection.BindingFlags filter = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
            return type.GetMethod("Parse", filter, null, new Type[] { typeof(string) }, null) != null;
        }

        private static object ParseTypeLiteralValue(Type type, string typevalue, object default_value)
        {
            if (type.IsEnum)
            {
                if (typevalue == null && default_value == null)
                    default_value = Activator.CreateInstance(type);
                try
                {
                    return Enum.Parse(type, typevalue == null ? default_value.ToString() : typevalue, true);
                }
                catch (System.ArgumentException exception)
                {
                    throw new ArgumentException(string.Format("Type {0} mismatch for literal value {1}.", type.FullName, typevalue), exception);
                }
            }
            if (HasSingleStringConstructor(type))
                return StringConstructibleInstance(type, typevalue, default_value);
            if (HasStaticStringParseMethod(type))
                return FromParseMethod(type, typevalue, default_value);
            if (type.IsPrimitive || type.IsValueType || type == typeof(string))
                return Convert.ChangeType(typevalue == null ? default_value : typevalue, type);
            throw new Exception(string.Format("The data type for literal {0} is not supported yet:{1}", typevalue, type.FullName));
        }

        private static object StringConstructibleInstance(Type type, string typevalue, object default_value)
        {
            try
            {
                default_value = default_value != null ? default_value.ToString() : default_value;
                if (typevalue == null)
                    typevalue = (string)default_value;
                return Activator.CreateInstance(type, typevalue);
            }
            catch (System.Reflection.TargetInvocationException exception)
            {
                throw exception.InnerException;
            }
        }

        private static object FromParseMethod(Type type, string typevalue, object default_value)
        {
            try
            {
                default_value = default_value != null ? default_value.ToString() : default_value;
                if (typevalue == null)
                    typevalue = (string)default_value;
                System.Reflection.BindingFlags filter = System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
                return type.InvokeMember("Parse", filter, null, null, new object[] { typevalue });
            }
            catch (System.Reflection.TargetInvocationException exception)
            {
                throw exception.InnerException;
            }
        }

        private static bool IsDictionary(Type type)
        {
            Type[] roles = type.FindInterfaces((atype, gauge) => atype.ToString() == gauge.ToString(), "System.Collections.IDictionary");
            return roles.Length == 1;
        }

        private static bool IsGenericSingleTypeParameterCollection(Type type)
        {
            Type[] roles = type.FindInterfaces((atype, gauge) => atype.ToString().StartsWith(gauge.ToString()), "System.Collections.Generic.ICollection`1");
            return roles.Length == 1;
        }

        private static object ParseArrayOfLiterals<T>(IList<string> items, Type element_type, T member_name_or_index, object default_value)
        {
            Array array = Array.CreateInstance(element_type, items.Count);
            for (int k = 0; k < items.Count; ++k)
                array.SetValue(ParseTypeLiteralValue(element_type, items[k], default_value), k);
            return array;
        }

        private static object ParseArrayOfObjects(string multicommandline, Type element_type)
        {
            Array array = null;
            string[] args_per_object = ParseMultiCommandLine(multicommandline);
            if (args_per_object != null && args_per_object.Length > 0)
            {
                array = Array.CreateInstance(element_type, args_per_object.Length);
                for (int k = 0; k < args_per_object.Length; ++k)
                {
                    string[] objectargs = ParseCommandLine(args_per_object[k]);
                    object array_object = Switch.AsType(objectargs, element_type);
                    array.SetValue(Convert.ChangeType(array_object, element_type), k);
                }
            }
            return array;
        }

        private static string[] ParseMultiCommandLine(string line)
        {
            return string.IsNullOrEmpty(line) ? null : line.Split(';');
        }

        private static string[] ParseCommandLine(string line)
        {
            return SystemArgumentParser.Parse(line);
        }

        private static object get_default_value(Type type)
        {
            if (type.IsValueType)
                return 0;
            return null;
        }

        private static object ParseDictionary<T>(Switch opts, Type member_type, T member_name_or_index, object default_value, int namespace_id)
        {
            Type[] map_arg_types = member_type.GetGenericArguments();
            if (map_arg_types.Length != 2)
                throw new ArgumentException("Cannot ParseDictionary because member_type does not has exactly two generic argument types", member_name_or_index + ":" + member_type.FullName);
            Type keytype = map_arg_types[0];
            Type valuetype = map_arg_types[1];
            object key_default_value = get_default_value(keytype);
            object value_default_value = get_default_value(valuetype);
            string subcommand_line = opts.GetValueByNameOrIndex(member_name_or_index);
            if (string.IsNullOrEmpty(subcommand_line))
                throw new ArgumentException("Cannot ParseDictionary out of an empty or null value.", member_name_or_index.ToString());
            string[] KeyValuePairs = ParseCommandLine(subcommand_line);
            var sub_opts = new Switch(KeyValuePairs);
            var result = Activator.CreateInstance(member_type) as System.Collections.IDictionary;
            if (result == null)
                throw new ArgumentException("Cannot ParseDictionary because member_type cannot be casted to IDictionary interface", member_name_or_index + ":" + member_type.FullName);
            Function<Switch, string, IList<string>> itemprovider = (args, name) => new List<string>(args.SuffixOf(name));
            foreach (KeyValuePair<string, string> keyvaluepair in sub_opts.NamedArguments)
                result.Add(ParseTypeLiteralValue(keytype, keyvaluepair.Key, key_default_value), ParseTypeValue<string>(sub_opts, keyvaluepair.Value, valuetype, keyvaluepair.Key, value_default_value, itemprovider, namespace_id));
            return result;
        }

        private static object ParseGenericCollectionOfLiterals(IList<string> items, Type collection_type, Type element_type, object default_element_value)
        {
            object collection = Activator.CreateInstance(collection_type);
            for (int k = 0; k < items.Count; ++k)
            {
                object element_value = ParseTypeLiteralValue(element_type, items[k], default_element_value);
                collection_type.InvokeMember("Add", System.Reflection.BindingFlags.InvokeMethod, null, collection, new object[] { element_value });
            }
            return collection;
        }

        private static object ParseGenericCollectionOfObjects(string multicommandline, Type collection_type, Type element_type)
        {
            object collection = null;
            string[] args_per_object = ParseMultiCommandLine(multicommandline);
            if (args_per_object != null && args_per_object.Length > 0)
            {
                collection = Activator.CreateInstance(collection_type);
                for (int k = 0; k < args_per_object.Length; ++k)
                {
                    string[] objectargs = ParseCommandLine(args_per_object[k]);
                    object element_object = Switch.AsType(objectargs, element_type);
                    collection_type.InvokeMember("Add", System.Reflection.BindingFlags.InvokeMethod, null, collection, new object[] { element_object });
                }
            }
            return collection;
        }

        public int Count
        {
            //TODO By now returns the names from the default namespace
            get { return (this.namespaces.ContainsKey(1) ? this.namespaces[1].Count : 0) + this.positioned.Count; }
        }

        public System.Collections.ObjectModel.ReadOnlyCollection<string> IndexedArguments
        {
            get
            {
                if (readonly_values == null)
                    readonly_values = new System.Collections.ObjectModel.ReadOnlyCollection<string>(this.positioned.ConvertAll((from) => from.Value));
                return readonly_values;
            }
        }

        public System.Collections.ObjectModel.ReadOnlyCollection<KeyValuePair<string, string>> NamedArguments
        {
            get
            {
                if (readonly_switches == null)
                {
                    IList<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
                    int namespace_id = 1; //TODO By now returns the names from the default namespace
                    if (this.namespaces.ContainsKey(namespace_id))
                    {
                        var named = this.namespaces[namespace_id];
                        foreach (NamedArgument arg in named.Values)
                            list.Add(new KeyValuePair<string, string>(arg.Name.Value, arg.Turn.Matched ? arg.Turn.Value : arg.Value.Value));
                    }
                    readonly_switches = new System.Collections.ObjectModel.ReadOnlyCollection<KeyValuePair<string, string>>(list);
                }
                return readonly_switches;
            }
        }

        public string this[int index]
        {
            get
            {
                return index < positioned.Count ? positioned[index].Value : null;
            }
        }

        public string this[string option_name, int namespace_id = 1]
        {
            get
            {
                if (this.namespaces.ContainsKey(namespace_id) && this.namespaces[namespace_id].ContainsConflatedKey(option_name))
                {
                    NamedArgument arg = this.namespaces[namespace_id].GetConflatedValue(option_name);
                    if (arg.Turn.Matched)
                        return arg.Turn.Value;
                    return arg.Value.Matched ? arg.Value.Value : null;
                }
                else
                    return null;
            }
        }

        private string GetValueByNameOrIndex<T>(T name_or_index)
        {
            return name_or_index is int ? this[Convert.ToInt32(name_or_index)] : this[Convert.ToString(name_or_index)];
        }

        public Dictionary<Type, utility.Switch.InstanceCreator> ConstructorMap
        {
            get { return creators; }
            set { creators = value; }
        }

        public void WriteAllAsTable(System.IO.TextWriter output)
        {
            if (IndexedArguments.Count > 0)
            {
                output.WriteLine("Positioned arguments:");
                foreach (string arg in IndexedArguments)
                    output.WriteLine("\t{0}", arg);
            }
            if (NamedArguments.Count > 0)
            {
                output.WriteLine("Named arguments or Switches from namespace 1:"); //TODO By now returns the names from the default namespace
                foreach (var turn in NamedArguments)
                    output.WriteLine("\t{0} = {1}", turn.Key, turn.Value);
            }
        }

        public bool Is(string option_name, int namespace_id = 1)
        {
            if (this.namespaces.ContainsKey(namespace_id) && this.namespaces[namespace_id].ContainsConflatedKey(option_name))
            {
                NamedArgument arg = this.namespaces[namespace_id].GetConflatedValue(option_name);
                if (arg.Turn.Matched)
                    return arg.Turn.Value == ON;
                return true;
            }
            else
                return false;
        }

        public static void ShowUsage(Type type)
        {
            Escape.Write("\n$yellow|Available operation arguments:\n");
            string exclude = "ToString,Equals,GetHashCode,GetType";
            foreach (System.Reflection.MethodInfo m in System.Linq.Enumerable.Where(type.GetMethods(), (method) => exclude.IndexOf(method.Name) < 0))
            {
                var usage = new System.Text.StringBuilder(m.Name + " ");
                object[] attrs = m.GetCustomAttributes(typeof(UsageAttribute), false);
                if (attrs != null && attrs.Length > 0)
                {
                    System.Linq.Enumerable.Aggregate(System.Linq.Enumerable.Cast<UsageAttribute>(attrs), usage, (whole, next) => { whole.AppendFormat("-{0} ", next.Description); return whole; });
                }
                Escape.Write("\t$green|{0}\n", usage);
            }
            Escape.Write("\n$yellow|Available data arguments:\n");
            foreach (System.Reflection.FieldInfo f in type.GetFields())
            {
                show_argument_info(f.Name, f.FieldType);
            }
            foreach (System.Reflection.PropertyInfo p in type.GetProperties())
            {
                show_argument_info(p.Name, p.PropertyType);
            }
        }

        private static void show_argument_info(string name, Type type)
        {
            Escape.Write("\t$green|{0} : $cyan|{1}\n", name, type.FullName);
            if (type.IsEnum)
            {
                Escape.Write("\t\t$cyan|{0}\n", System.Linq.Enumerable.Aggregate(Enum.GetNames(type), new System.Text.StringBuilder(), (whole, next) => { whole.Append(next + ","); return whole; }));
            }
        }

        #region Iterators

        public IEnumerable<string> SuffixOf(string arg_name, int namespace_id = 1)
        {
            if (!(this.namespaces.ContainsKey(namespace_id) && this.namespaces[namespace_id].ContainsConflatedKey(arg_name)))
                yield break;
            NamedArgument arg = this.namespaces[namespace_id].GetConflatedValue(arg_name);
            for (int k = arg.AbsoluteIndex + 1; k < this.AllArgument.Count; ++k)
            {
                PositionedArgument next = this.AllArgument[k] as PositionedArgument;
                if (next != null)
                    yield return next.Value;
                else
                    yield break;
            }
        }

        public IEnumerable<KeyValuePair<string, string>> SuffixOf(int index)
        {
            if (!(index >= 0 && index < this.positioned.Count)) yield break;
            PositionedArgument arg = this.positioned[index];
            for (int k = arg.AbsoluteIndex + 1; k < this.AllArgument.Count; ++k)
            {
                NamedArgument next = this.AllArgument[k] as NamedArgument;
                if (next != null)
                    yield return new KeyValuePair<string, string>(next.Name.Value, next.Turn.Matched ? next.Turn.Value : next.Value.Value);
                else
                    yield break;
            }
        }

        public IEnumerable<string> PrefixOf(string arg_name, int namespace_id = 1)
        {
            if (!(this.namespaces.ContainsKey(namespace_id) && this.namespaces[namespace_id].ContainsConflatedKey(arg_name)))
                yield break;
            NamedArgument arg = this.namespaces[namespace_id].GetConflatedValue(arg_name);
            for (int k = arg.AbsoluteIndex - 1; k >= 0; --k)
            {
                PositionedArgument prev = this.AllArgument[k] as PositionedArgument;
                if (prev != null)
                    yield return prev.Value;
                else
                    yield break;
            }
        }

        public IEnumerable<KeyValuePair<string, string>> PrefixOf(int index)
        {
            if (!(index >= 0 && index < this.positioned.Count)) yield break;
            PositionedArgument arg = this.positioned[index];
            for (int k = arg.AbsoluteIndex - 1; k >= 0; --k)
            {
                NamedArgument prev = this.AllArgument[k] as NamedArgument;
                if (prev != null)
                    yield return new KeyValuePair<string, string>(prev.Name.Value, prev.Turn.Matched ? prev.Turn.Value : prev.Value.Value);
                else
                    yield break;
            }
        }

        #endregion

        #region IEnumerable<string> Members

        public IEnumerator<string> GetEnumerator()
        {
            for (int k = 0; k < this.args.Length; ++k) yield return this.args[k];
        }

        #endregion

        #region IEnumerable Members

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            for (int k = 0; k < this.args.Length; ++k) yield return this.args[k];
        }

        #endregion
    }

    public class SystemArgumentParser
    {
        #region SimpleCommandLineParser
        class ParseState
        {
            public byte StateID;
            public Action<SimpleCommandLineParser> FetchAction;
        }

        class StateTable
        {
            private static Dictionary<byte, Dictionary<Input_Type, ParseState>> states;

            internal StateTable()
            {
                states = new Dictionary<byte, Dictionary<Input_Type, ParseState>>();
            }

            public ParseState this[byte state, Input_Type input]
            {
                get
                {
                    return states[state][input];
                }
                set
                {
                    if (!states.ContainsKey(state))
                        states[state] = new Dictionary<Input_Type, ParseState>();
                    states[state][input] = value;
                }
            }
        }

        static class StateTableSingleton
        {
            private static readonly StateTable table;

            static StateTableSingleton()
            {
                table = new StateTable();
                table[0, Input_Type.Space] = new ParseState() { StateID = 0, FetchAction = null };
                table[0, Input_Type.Character] = new ParseState() { StateID = 1, FetchAction = FetchChar };
                table[0, Input_Type.Quote] = new ParseState() { StateID = 2, FetchAction = null };

                table[1, Input_Type.Space] = new ParseState() { StateID = 0, FetchAction = FetchArgument };
                table[1, Input_Type.Character] = new ParseState() { StateID = 1, FetchAction = FetchChar };
                table[1, Input_Type.Quote] = new ParseState() { StateID = 2, FetchAction = null };

                table[2, Input_Type.Space] = new ParseState() { StateID = 3, FetchAction = FetchChar };
                table[2, Input_Type.Character] = new ParseState() { StateID = 3, FetchAction = FetchChar };
                table[2, Input_Type.Quote] = new ParseState() { StateID = 0, FetchAction = FetchArgument };

                table[3, Input_Type.Space] = new ParseState() { StateID = 3, FetchAction = FetchChar };
                table[3, Input_Type.Character] = new ParseState() { StateID = 3, FetchAction = FetchChar };
                table[3, Input_Type.Quote] = new ParseState() { StateID = 4, FetchAction = null };

                table[4, Input_Type.Space] = new ParseState() { StateID = 0, FetchAction = FetchArgument };
                table[4, Input_Type.Character] = new ParseState() { StateID = 3, FetchAction = FetchChar };
                table[4, Input_Type.Quote] = new ParseState() { StateID = 3, FetchAction = null };
            }

            public static StateTable Instance { get { return table; } }

            private static void FetchChar(SimpleCommandLineParser parser)
            {
                parser.FetchChar();
            }

            private static void FetchArgument(SimpleCommandLineParser parser)
            {
                parser.FetchArgument();
            }
        }

        enum Input_Type : byte
        {
            Space = 0,
            Character = 1,
            Quote = 2
        }

        class SimpleCommandLineParser
        {
            private string input;
            private List<string> result;
            private System.Text.StringBuilder currently__build_arg;
            private char input_char;
            private ParseState state;
            private StateTable state_table;

            public SimpleCommandLineParser(string input)
            {
                this.input = input;
                result = new List<string>();
                currently__build_arg = new System.Text.StringBuilder();
                state = new ParseState() { StateID = 0 };
                state_table = StateTableSingleton.Instance;
            }

            public string[] Parse()
            {
                for (int k = 0; k < input.Length; ++k)
                {
                    input_char = input[k];
                    Input_Type input_char_type = 0;
                    if (input_char == ' ')
                        input_char_type = Input_Type.Space;
                    else if (input_char == '"')
                        input_char_type = Input_Type.Quote;
                    else
                        input_char_type = Input_Type.Character;
                    state = state_table[state.StateID, input_char_type];
                    if (state.FetchAction != null)
                        state.FetchAction(this);
                }
                state = state_table[state.StateID, Input_Type.Space];
                if (state.FetchAction != null)
                    state.FetchAction(this);
                return result.ToArray();
            }

            internal void FetchChar()
            {
                currently__build_arg.Append(input_char);
            }

            internal void FetchArgument()
            {
                result.Add(currently__build_arg.ToString());
                currently__build_arg = new System.Text.StringBuilder();
            }
        }
        #endregion

        public static string[] Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Length == 0)
                throw new ArgumentException("Cannot parse an empty embedded command-line string");

#if !SYS_SUB_CMDLINE
            var parser = new SimpleCommandLineParser(input);
            return parser.Parse();
#endif

#if SYS_SUB_CMDLINE
      var allowance = new System.Security.Permissions.SecurityPermission(System.Security.Permissions.SecurityPermissionFlag.UnmanagedCode);
      allowance.Assert();
      IntPtr argv = IntPtr.Zero;
      try
      {
        int argc;
        argv = CommandLineToArgvW(input, out argc);
        if (argv == IntPtr.Zero)
          throw new ArgumentException("Unable to parse arguments", input, new System.ComponentModel.Win32Exception());
        string[] args = new string[argc];
        for (int k = 0; k < argc; ++k)
          args[k] = System.Runtime.InteropServices.Marshal.PtrToStringUni
          (
            System.Runtime.InteropServices.Marshal.ReadIntPtr(argv, k * IntPtr.Size)
          );
        return args;
      }
      finally
      {
        LocalFree(argv);
        System.Security.CodeAccessPermission.RevertAssert();
      }
#endif
        }

#if SYS_SUB_CMDLINE
    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
    static extern IntPtr CommandLineToArgvW([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string commandline, out int argc);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr hMem);
#endif
    }

    #endregion
    #region Switch - design strategy v1.0
    //		public class Switch : IEnumerable
    //		{
    //			public Switch(string[] args)
    //			{
    //				itsArgs = args;
    //			}
    //			public bool isON(string option)
    //			{
    //				return ((IList)itsArgs).Contains(option);
    //			}
    //			public void writeTo(TextWriter writer,string format)
    //			{
    //				foreach(string s in itsArgs)
    //				{
    //					writer.Write(format,s);
    //				}
    //			}
    //
    //			IEnumerator IEnumerable.GetEnumerator()
    //			{
    //				return itsArgs.GetEnumerator();
    //			}
    //
    //			private string[] itsArgs;
    //		}
    #endregion
}