namespace utility
{
	using System;
	using System.Collections.Generic;
	using System.Text.RegularExpressions;

	public class Switch : IEnumerable<string>
	{
		private const string ON = "+";
		private const string OFF = "-";

		private string[] args;
		private Dictionary<string, Argument> named;
		private List<string> positioned;
		private System.Collections.ObjectModel.ReadOnlyCollection<string> readonly_values;
		private System.Collections.ObjectModel.ReadOnlyCollection<KeyValuePair<string, string>> readonly_switches;

		internal class Option
		{
			public Option(string name, bool matched, string value) { Name = name; Matched = matched; Value = value; }
			public string Name, Value;
			public bool Matched;
		}

		internal class Argument
		{
			public Argument(Option name, Option turn, Option value) { Name = name; Turn = turn; Value = value; }
			public Argument(Group name, Group turn, Group value)
			{
				Name = new Option("name", name.Success, name.Value);
				Turn = new Option("turn", turn.Success, turn.Value);
				Value = new Option("value", value.Success, value.Value);
			}
			public Option Name, Turn, Value;
		}


		public Switch(string[] opts) : this(opts, StringComparer.CurrentCultureIgnoreCase) { }
		public Switch(string[] opts, StringComparer comparer)
		{
			this.args = opts.Clone() as string[];
			this.named = new Dictionary<string, Argument>(comparer);
			this.positioned = new List<string>();
			string regexpr = @"[-/](?<name>\w+)((?<turn>[+-])|[:=](?<value>.*)){0,1}";
			System.Text.RegularExpressions.Regex expr = new System.Text.RegularExpressions.Regex(regexpr, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			for(int k = 0; k < opts.Length; ++k)
			{
				string arg = opts[k];
				Match match = expr.Match(arg);
				Group name = match.Groups["name"];
				Argument argument = new Argument(name, match.Groups["turn"], match.Groups["value"]);
				if(name.Success)
					this.named[name.Value] = argument;
				else
					this.positioned.Add(arg);
			}
		}

		public int Count
		{
			get { return this.named.Count + this.positioned.Count; }
		}

		public System.Collections.ObjectModel.ReadOnlyCollection<string> Arguments
		{
			get
			{
				if(readonly_values == null)
					readonly_values = new System.Collections.ObjectModel.ReadOnlyCollection<string>(this.positioned);
				return readonly_values;
			}
		}

		public System.Collections.ObjectModel.ReadOnlyCollection<KeyValuePair<string, string>> Switches
		{
			get
			{
				if(readonly_switches == null)
				{
					IList<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
					foreach(Argument arg in this.named.Values) list.Add(new KeyValuePair<string, string>(arg.Name.Value, arg.Turn.Matched ? arg.Turn.Value : arg.Value.Value));
					readonly_switches = new System.Collections.ObjectModel.ReadOnlyCollection<KeyValuePair<string, string>>(list);
				}
				return readonly_switches;
			}
		}

		public string this[int index]
		{
			get
			{
				return index < positioned.Count ? positioned[index] : null;
			}
		}

		public string this[string option_name]
		{
			get
			{
//				option_name = tocase(option_name);
				if(this.named.ContainsKey(option_name))
				{
					Argument arg = this.named[option_name];
					if(arg.Turn.Matched)
						return arg.Turn.Value;
					return arg.Value.Matched ? arg.Value.Value : null;
				}
				else
					return null;
			}
		}

		public bool Is(string option_name)
		{
			if(this.named.ContainsKey(option_name))
			{
				Argument arg = this.named[option_name];
				if(arg.Turn.Matched)
					return arg.Turn.Value == ON;
				return true;
			}
			else
				return false;
		}

		#region IEnumerable<string> Members

		public IEnumerator<string> GetEnumerator()
		{
			for(int k = 0; k < this.args.Length; ++k) yield return this.args[k];
		}

		#endregion

		#region IEnumerable Members

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			for(int k = 0; k < this.args.Length; ++k) yield return this.args[k];
		}

		#endregion
	}
}