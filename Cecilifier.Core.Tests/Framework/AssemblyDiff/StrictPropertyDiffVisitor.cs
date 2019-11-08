using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    internal class StrictPropertyDiffVisitor : IPropertyDiffVisitor
    {
        private readonly TextWriter _output;

        public StrictPropertyDiffVisitor(TextWriter output)
        {
            _output = output;
        }

        public PropertyDefinition VisitProperty(PropertyDefinition sourceProperty, TypeDefinition target)
        {
            var property = target.Properties.SingleOrDefault(prop => prop.FullName == sourceProperty.FullName);
            if (property == null)
            {
                _output.WriteLine($"Property '{sourceProperty.FullName}' not found in type '{target.FullName}'");
            }
            return property;
        }

        public bool VisitType(PropertyDefinition source, PropertyDefinition target)
        {
            var ret = source.PropertyType.FullName == target.PropertyType.FullName;
            if (!ret)
                _output.WriteLine($"Types of property '{source.Name}' differs. Expected '{source.PropertyType.FullName}' but got '{target.PropertyType.FullName}'");
                
            return ret;
        }

        public bool VisitAttributes(PropertyDefinition source, PropertyDefinition target)
        {
            var ret = source.Attributes == target.Attributes;
            if (!ret)
                _output.WriteLine($"Attributes of event '{source.Name}' differs. Expected '{source.Attributes}' but got '{target.Attributes}'");
            
            return ret;
        }

        public bool VisitAccessors(PropertyDefinition source, PropertyDefinition target)
        {
            bool ret = true;
            if (source.GetMethod?.FullName != target.GetMethod?.FullName)
            {
                _output.WriteLine($"GetMethod differs: Expected '{source.GetMethod}' but got '{target.GetMethod}'");
            }
            
            if (source.SetMethod?.FullName != target.SetMethod?.FullName)
            {
                _output.WriteLine($"SetMethod differs: Expected '{source.SetMethod}' but got '{target.SetMethod}'");
            }

            return ret;
        }

        public bool VisitCustomAttributes(PropertyDefinition source, PropertyDefinition target)
        {
            return Utils.CheckCustomAttributes(_output, source, target);
        }
    }
}
