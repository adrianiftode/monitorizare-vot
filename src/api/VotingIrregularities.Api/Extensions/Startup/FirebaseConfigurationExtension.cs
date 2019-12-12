using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using VotingIrregularities.Api.Options;

namespace VotingIrregularities.Api.Extensions.Startup
{
    public static class FirebaseConfigurationExtension
    {
        public static void ConfigurePrivateKey(IConfigurationRoot configuration)
        {
            var firebaseOptions = configuration.GetSection(nameof(FirebaseServiceOptions));
            var privateKeyPath = firebaseOptions[nameof(FirebaseServiceOptions.ServerKey)];
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", Path.GetFullPath(privateKeyPath));
        }
    }
}