using System.Xml;
using System.Text;

var doc = new XmlDocument();
doc.Load("~/.nuget/packages/system.reflection.primitives/4.3.0/ref/netcore50/System.Reflection.Primitives.xml");

var root = doc.DocumentElement;
var opcodes = root.SelectNodes("members/member");

var sb = new StringBuilder(@"let opCodes = [
");
foreach(var opcode in opcodes.OfType<XmlNode>().Where(c => c.Attributes[0].Value.Contains("OpCodes.")))
        sb.AppendLine($"{{ name : \"{opcode.Attributes[0].Value.Substring(33)}\", description: \"{opcode.LastChild.InnerText}\" }},");

sb.AppendLine("]; ");
Console.WriteLine(sb.ToString());