namespace Cecilifier.Core.Misc
{
	public class TypeInfo
	{
		public readonly string LocalVariable;

		public TypeInfo(string localVariable)
		{
			LocalVariable = localVariable;
		}

		public override string ToString()
		{
			return LocalVariable;
		}
	}
}