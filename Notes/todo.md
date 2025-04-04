1. Add issue to improve caching of delegate/lambda instantiations 
   1. As of today only conversion from `static methods` to delegates are cached but I think C# caches all instantiations
   2. See CecilDefinitionsFactory.InstantiateDelegate(). Maybe a simple approach would be to not check StaticDelegateCacheContext.IsStaticDelegate ?
1. 