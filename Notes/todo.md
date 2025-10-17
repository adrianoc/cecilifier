## SRM

1. Investigate bizarre, not used member references. For instance:
   > var l_propertyAccessors_P_getRef_12 = metadata.AddMemberReference(
		                                    cls_propertyAccessors_5,
		                                    metadata.GetOrAddString("PropertyAccessors.P.get"),
		                                    l_methodSignature_11);
1. Type/member references to type/members defined in the same assembly seems to being qualified with the assembly name
   1. this seems wrong but the code seems to work
   1. Does this happen because we are referencing these type/member through references instead of type/member definitions?
   1. Is it even possible to use the type/member definitions?

## General


1. Add issue to improve caching of delegate/lambda instantiations 
   1. As of today only conversion from `static methods` to delegates are cached but I think C# caches all instantiations
   2. See CecilDefinitionsFactory.InstantiateDelegate(). Maybe a simple approach would be to not check StaticDelegateCacheContext.IsStaticDelegate ?
1. Improve naming in code generation
   1. Change property setter method variable to use `method` instead of `local` (they are being named as `l_set_nn` instead of `m_set_nn` as the getters)