using System;
using System.Collections.Generic;

namespace mncpublishwithaes
{
    public static class StaticData
    {
        public static readonly string Issuer = "myIssuer";
        public static readonly string Audience = "myAudience";
        public static readonly string ContentKeyPolicyName = "SharedContentKeyPolicyUsedByAllAssets";

        public static readonly Dictionary<string, Guid> ContentKeyIds;

        static StaticData()
        {
            ContentKeyIds = new Dictionary<string, Guid>();
        }
    }
}
