﻿using LetsEncrypt.ACME.DNS;
using LetsEncrypt.ACME.JOSE;
using LetsEncrypt.ACME.PKI;
using LetsEncrypt.ACME.WebServer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace LetsEncrypt.ACME
{
    [TestClass]
    public class AcmeClientUnitTests
    {
        public const string BASE_LOCAL_STORE = "..\\lostore\\";

        // Running against a local (private) instance of Boulder CA
        //Uri _rootUrl = new Uri("http://acme2.aws3.ezshield.ws:4000/");
        //string _dirUrlBase = "http://localhost:4000/";

        // Running against the STAGE (public) instance of Boulder CA
        Uri _rootUrl = new Uri("https://acme-staging.api.letsencrypt.org/");
        string _dirUrlBase = "https://acme-staging.api.letsencrypt.org/";

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0010_Init()
        {
            using (var signer = new RS256Signer())
            {
                using (var client = new AcmeClient(_rootUrl, signer: signer))
                {
                    client.Init();

                    Assert.IsNotNull(client.Directory);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(client.NextNonce));
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0020_GetDirectory()
        {
            var boulderResMap = new Dictionary<string, string>
            {
                ["new-authz"]   /**/ = $"{_dirUrlBase}acme/new-authz",
                ["new-cert"]    /**/ = $"{_dirUrlBase}acme/new-cert",
                ["new-reg"]     /**/ = $"{_dirUrlBase}acme/new-reg",
                ["revoke-cert"] /**/ = $"{_dirUrlBase}acme/revoke-cert",
            };

            using (var signer = new RS256Signer())
            {
                using (var client = new AcmeClient(_rootUrl, signer: signer))
                {
                    client.Init();

                    // Test absolute URI paths
                    var acmeDirAbs = client.GetDirectory(false);
                    foreach (var ent in boulderResMap)
                    {
                        Assert.IsTrue(acmeDirAbs.Contains(ent.Key));
                        Assert.AreEqual(ent.Value, acmeDirAbs[ent.Key]);
                    }

                    // Test relative URI paths
                    var acmeDirRel = client.GetDirectory(true);
                    foreach (var ent in boulderResMap)
                    {
                        var relUrl = ent.Value.Replace(_dirUrlBase, "/");
                        Assert.IsTrue(acmeDirRel.Contains(ent.Key));
                        Assert.AreEqual(relUrl, acmeDirRel[ent.Key]);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0030_Register()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Init();

                    client.GetDirectory(true);

                    client.Register(new string[]
                            {
                                "mailto:letsencrypt@mailinator.com",
                                "tel:+14109361212",
                            });

                    Assert.IsNotNull(client.Registration);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(client.Registration.RegistrationUri));

                    using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Create))
                    {
                        client.Registration.Save(fs);
                    }
                }
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Create))
                {
                    signer.Save(fs);
                }
            }
        }

        /// <summary>
        /// An <i>empty update</i> does not request any registration data elements be
        /// updated and should simply return the current state of the target registration
        /// (<see cref="https://letsencrypt.github.io/acme-spec/#rfc.section.6.3">ACME
        /// spec 6.3</see>).
        /// </summary>
        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0040_RegisterEmptyUpdate()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    // Do a simple update with no data changes requested
                    client.UpdateRegistration(true);

                    Assert.IsNotNull(client.Registration);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(client.Registration.RegistrationUri));

                    using (var fs = new FileStream("..\\TestRegisterUpdate.acmeReg", FileMode.Create))
                    {
                        client.Registration.Save(fs);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0050_RegisterUpdateTosAgreement()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    client.UpdateRegistration(true, true);

                    Assert.IsNotNull(client.Registration);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(client.Registration.RegistrationUri));

                    using (var fs = new FileStream("..\\TestRegisterUpdate.acmeReg", FileMode.Create))
                    {
                        client.Registration.Save(fs);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0060_RegisterUpdateContacts()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    client.UpdateRegistration(true, contacts: new string[]
                            {
                                "mailto:letsencrypt+update@mailinator.com",
                            });

                    Assert.IsNotNull(client.Registration);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(client.Registration.RegistrationUri));

                    using (var fs = new FileStream("..\\TestRegisterUpdate.acmeReg", FileMode.Create))
                    {
                        client.Registration.Save(fs);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0070_RegisterDuplicate()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegisterDuplicate.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Init();

                    client.GetDirectory(true);

                    try
                    {
                        client.Register(new string[]
                                {
                                    "mailto:letsencrypt+dup@mailinator.com",
                                    "tel:+14105551212",
                                });
                        Assert.Fail("WebException expected");
                    }
                    catch (AcmeClient.AcmeWebException ex)
                    {
                        Assert.IsNotNull(ex.WebException);
                        Assert.IsNotNull(ex.Response);
                        Assert.AreEqual(HttpStatusCode.Conflict, ex.Response.StatusCode);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0080_AuthorizeDnsBlacklisted()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    try
                    {
                        client.AuthorizeIdentifier("foo.example.com");
                    }
                    catch (AcmeClient.AcmeWebException ex)
                    {
                        Assert.IsNotNull(ex.WebException);
                        Assert.IsNotNull(ex.Response);
                        Assert.IsNotNull(ex.Response.ProblemDetail);
                        Assert.AreEqual(HttpStatusCode.Forbidden, ex.Response.StatusCode);
                        Assert.AreEqual("urn:acme:error:unauthorized", ex.Response.ProblemDetail.Type);
                        StringAssert.Contains(ex.Response.ProblemDetail.Detail, "blacklist");
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0090_AuthorizeIdentifier()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    var authzState = client.AuthorizeIdentifier("foo.letsencrypt.cc");

                    foreach (var c in authzState.Challenges)
                    {
                        if (c.Type == "dns")
                        {
                            var dnsResponse = c.GenerateDnsChallengeAnswer(
                                    authzState.Identifier, signer);
                        }
                    }

                    using (var fs = new FileStream("..\\TestAuthz.acmeAuthz", FileMode.Create))
                    {
                        authzState.Save(fs);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0100_RefreshAuthzDnsChallenge()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    AuthorizationState authzState;
                    using (var fs = new FileStream("..\\TestAuthz.acmeAuthz", FileMode.Open))
                    {
                        authzState = AuthorizationState.Load(fs);
                    }

                    client.RefreshAuthorizeChallenge(authzState, "dns", true);

                    using (var fs = new FileStream("..\\TestAuthz-DnsChallengeRefreshed.acmeAuthz", FileMode.Create))
                    {
                        authzState.Save(fs);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0110_RefreshAuthzHttpChallenge()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    AuthorizationState authzState;
                    using (var fs = new FileStream("..\\TestAuthz.acmeAuthz", FileMode.Open))
                    {
                        authzState = AuthorizationState.Load(fs);
                    }

                    client.RefreshAuthorizeChallenge(authzState, "simpleHttp", true);

                    using (var fs = new FileStream("..\\TestAuthz-HttpChallengeRefreshed.acmeAuthz", FileMode.Create))
                    {
                        authzState.Save(fs);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0120_GenerateChallengeAnswers()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    AuthorizationState authzState;
                    using (var fs = new FileStream("..\\TestAuthz.acmeAuthz", FileMode.Open))
                    {
                        authzState = AuthorizationState.Load(fs);
                    }

                    client.GenerateAuthorizeChallengeAnswer(authzState, "dns");
                    client.GenerateAuthorizeChallengeAnswer(authzState, "simpleHttp");

                    using (var fs = new FileStream("..\\TestAuthz-ChallengeAnswers.acmeAuthz", FileMode.Create))
                    {
                        authzState.Save(fs);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        [Timeout(120 * 1000)]
        public void Test0130_HandleDnsChallenge()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    AuthorizationState authzState;
                    using (var fs = new FileStream("..\\TestAuthz-ChallengeAnswers.acmeAuthz", FileMode.Open))
                    {
                        authzState = AuthorizationState.Load(fs);
                    }

                    client.GenerateAuthorizeChallengeAnswer(authzState, "dns");
                    client.SubmitAuthorizeChallengeAnswer(authzState, "dns", true);

                    using (var fs = new FileStream("..\\TestAuthz-DnsChallengeAnswered.acmeAuthz", FileMode.Create))
                    {
                        authzState.Save(fs);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0145_SubmitHttpChallengeAnswers()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    AuthorizationState authzState;
                    using (var fs = new FileStream("..\\TestAuthz-ChallengeAnswers.acmeAuthz", FileMode.Open))
                    {
                        authzState = AuthorizationState.Load(fs);
                    }

                    client.GenerateAuthorizeChallengeAnswer(authzState, "simpleHttp");
                    client.SubmitAuthorizeChallengeAnswer(authzState, "simpleHttp", true);

                    using (var fs = new FileStream("..\\TestAuthz-HttpChallengeAnswered.acmeAuthz", FileMode.Create))
                    {
                        authzState.Save(fs);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void TestRequestCertificateInvalidCsr()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    try
                    {
                        client.RequestCertificate("FOOBARNON");
                        Assert.Fail("WebException expected");
                    }
                    catch (AcmeClient.AcmeWebException ex)
                    {
                        Assert.IsNotNull(ex.WebException);
                        Assert.IsNotNull(ex.Response);
                        Assert.AreEqual(HttpStatusCode.BadRequest, ex.Response.StatusCode);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0170_GenCsrAndRequestCertificate()
        {
            var rsaKeys = CsrHelper.GenerateRsaPrivateKey();
            using (var fs = new FileStream("..\\TestGenCsr-rsaKeys.txt", FileMode.Create))
            {
                rsaKeys.Save(fs);
            }

            var csrDetails = new CsrHelper.CsrDetails
            {
                CommonName = "foo.letsencrypt.cc"
            };
            var csr = CsrHelper.GenerateCsr(csrDetails, rsaKeys);
            using (var fs = new FileStream("..\\TestGenCsr-csrDetails.txt", FileMode.Create))
            {
                csrDetails.Save(fs);
            }
            using (var fs = new FileStream("..\\TestGenCsr-csr.txt", FileMode.Create))
            {
                csr.Save(fs);
            }

            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                byte[] derRaw;
                using (var bs = new MemoryStream())
                {
                    csr.ExportAsDer(bs);
                    derRaw = bs.ToArray();
                }
                var derB64u = JwsHelper.Base64UrlEncode(derRaw);

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    var certRequ = client.RequestCertificate(derB64u);

                    using (var fs = new FileStream("..\\TestCertRequ.acmeCertRequ", FileMode.Create))
                    {
                        certRequ.Save(fs);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0180_RequestCertificate()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                var csrRaw = File.ReadAllBytes("..\\test-csr.der");
                var csrB64u = JwsHelper.Base64UrlEncode(csrRaw);

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    var certRequ = client.RequestCertificate(csrB64u);

                    using (var fs = new FileStream("..\\TestCertRequ.acmeCertRequ", FileMode.Create))
                    {
                        certRequ.Save(fs);
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("skipCI")]
        public void Test0190_RefreshCertificateRequest()
        {
            using (var signer = new RS256Signer())
            {
                signer.Init();
                using (var fs = new FileStream("..\\TestRegister.acmeSigner", FileMode.Open))
                {
                    signer.Load(fs);
                }

                AcmeRegistration reg;
                using (var fs = new FileStream("..\\TestRegister.acmeReg", FileMode.Open))
                {
                    reg = AcmeRegistration.Load(fs);
                }

                //var csrRaw = File.ReadAllBytes($"{BASE_LOCAL_STORE}test-csr.der");
                //var csrB64u = JwsHelper.Base64UrlEncode(csrRaw);

                using (var client = new AcmeClient())
                {
                    client.RootUrl = _rootUrl;
                    client.Signer = signer;
                    client.Registration = reg;
                    client.Init();

                    client.GetDirectory(true);

                    CertificateRequest certRequ;
                    using (var fs = new FileStream("..\\TestCertRequ.acmeCertRequ", FileMode.Open))
                    {
                        certRequ = CertificateRequest.Load(fs);
                    }

                    client.RefreshCertificateRequest(certRequ, true);

                    using (var fs = new FileStream("..\\TestCertRequ-Refreshed.acmeCertRequ", FileMode.Create))
                    {
                        certRequ.Save(fs);
                    }

                    if (!string.IsNullOrEmpty(certRequ.CertificateContent))
                    {
                        using (var fs = new FileStream("..\\TestCertRequ-Refreshed.cer", FileMode.Create))
                        {
                            certRequ.SaveCertificate(fs);
                        }
                    }
                }
            }
        }
    }
}
