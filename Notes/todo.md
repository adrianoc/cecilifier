1. Add issue to improve caching of delegate/lambda instantiations 
   1. As of today only conversion from `static methods` to delegates are cached but I think C# caches all instantiations
   2. See CecilDefinitionsFactory.InstantiateDelegate(). Maybe a simple approach would be to not check StaticDelegateCacheContext.IsStaticDelegate ?
1. Improve naming in code generation
   1. Change property setter method variable to use `method` instead of `local` (they are being named as `l_set_nn` instead of `m_set_nn` as the getters)