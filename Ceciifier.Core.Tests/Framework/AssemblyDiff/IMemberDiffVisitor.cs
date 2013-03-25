﻿using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
	interface IMemberDiffVisitor
	{
		bool VisitMissing(IMemberDefinition member, TypeDefinition target);
		bool VisitName(IMemberDefinition source, IMemberDefinition target);
		bool VisitDeclaringType(IMemberDefinition source, IMemberDefinition target);
	}
}
