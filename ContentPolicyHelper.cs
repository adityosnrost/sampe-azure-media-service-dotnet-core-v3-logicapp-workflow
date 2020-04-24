using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Rest;
using Microsoft.Rest.Azure.Authentication;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Security.Cryptography;
using System.Linq;

namespace mncpublishwithaes
{
    public static class ContentPolicyHelper
    {
        public static async Task<ServiceClientCredentials> GetCredentialsAsync(ConfigWrapper config)
        {
            // Use ApplicationTokenProvider.LoginSilentWithCertificateAsync or UserTokenProvider.LoginSilentAsync to get a token using service principal with certificate
            //// ClientAssertionCertificate
            //// ApplicationTokenProvider.LoginSilentWithCertificateAsync

            // Use ApplicationTokenProvider.LoginSilentAsync to get a token using a service principal with symmetric key
            ClientCredential clientCredential = new ClientCredential(config.AadClientId, config.AadSecret);
            return await ApplicationTokenProvider.LoginSilentAsync(config.AadTenantId, clientCredential, ActiveDirectoryServiceSettings.Azure);
        }

        public static async Task<IAzureMediaServicesClient> CreateMediaServicesClientAsync(ConfigWrapper config)
        {
            var credentials = await GetCredentialsAsync(config);

            return new AzureMediaServicesClient(config.ArmEndpoint, credentials)
            {
                SubscriptionId = config.SubscriptionId,
            };
        }

        public static ContentKeyPolicy GetOrCreateContentKeyPolicy(
            IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string contentKeyPolicyName,
            byte[] randomBytes = null)
        {
            ContentKeyPolicy policy = client.ContentKeyPolicies.Get(resourceGroupName, accountName, contentKeyPolicyName);

            if (policy == null)
            {

                /************************************/
                /** Uncomment if using AES with JWT */
                if (randomBytes == null)
                {
                    randomBytes = new byte[40];
                    new RNGCryptoServiceProvider().GetBytes(randomBytes);
                }

                ContentKeyPolicySymmetricTokenKey primaryKey = new ContentKeyPolicySymmetricTokenKey(randomBytes);
                List<ContentKeyPolicyRestrictionTokenKey> alternateKeys = null;
                List<ContentKeyPolicyTokenClaim> requiredClaims = new List<ContentKeyPolicyTokenClaim>()
                {
                    ContentKeyPolicyTokenClaim.ContentKeyIdentifierClaim
                };

                List<ContentKeyPolicyOption> options = new List<ContentKeyPolicyOption>()
                {
                    new ContentKeyPolicyOption(
                        new ContentKeyPolicyClearKeyConfiguration(),
                        new ContentKeyPolicyTokenRestriction(StaticData.Issuer, StaticData.Audience, primaryKey, ContentKeyPolicyRestrictionTokenType.Jwt, alternateKeys, requiredClaims))
                };
                /************************************/

                /*************************************************/
                /** Uncomment if using AES with Open Restriction */
                //List<ContentKeyPolicyOption> options = new List<ContentKeyPolicyOption>()
                //{
                //    new ContentKeyPolicyOption(
                //        new ContentKeyPolicyClearKeyConfiguration(),
                //        new ContentKeyPolicyOpenRestriction())
                //};
                /*************************************************/

                // Since we are randomly generating the signing key each time, make sure to create or update the policy each time.
                // Normally you would use a long lived key so you would just check for the policies existence with Get instead of
                // ensuring to create or update it each time.
                policy = client.ContentKeyPolicies.CreateOrUpdate(resourceGroupName, accountName, contentKeyPolicyName, options);
            }

            return policy;
        }

        public static string GetToken(string issuer, string audience, string contentKeyId, ContentKeyPolicyProperties policyProperties)
        {
            var ckrestriction = (ContentKeyPolicyTokenRestriction)policyProperties.Options.FirstOrDefault()?.Restriction;
            var symKey = (ContentKeyPolicySymmetricTokenKey)ckrestriction.PrimaryVerificationKey;
            var tokenSigningKey = new SymmetricSecurityKey(symKey.KeyValue);

            //var tokenKey = new byte[40];
            //new RNGCryptoServiceProvider().GetBytes(tokenKey);
            //var tokenSigningKey = new SymmetricSecurityKey(tokenKey);

            SigningCredentials cred = new SigningCredentials(
                tokenSigningKey,
                // Use the  HmacSha256 and not the HmacSha256Signature option, or the token will not work!
                SecurityAlgorithms.HmacSha256,
                SecurityAlgorithms.Sha256Digest);

            Claim[] claims = new Claim[]
            {
                new Claim(ContentKeyPolicyTokenClaim.ContentKeyIdentifierClaim.ClaimType, contentKeyId)
            };

            JwtSecurityToken token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: DateTime.Now.AddMinutes(-5),
                expires: DateTime.Now.AddMinutes(60),
                signingCredentials: cred);

            JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();

            return handler.WriteToken(token);
        }

        public static void CleanUpContentPolicies(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string contentKeyPolicyName)
        {
            client.ContentKeyPolicies.Delete(resourceGroupName, accountName, contentKeyPolicyName);
        }
    }
}
