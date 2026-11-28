/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Netty.NET.Common;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: @ParameterizedTest @MethodSource(Stream<String>) is a [Theory] with MemberData rows; validUris() draws 1000
// random seeds from an unseeded SplittableRandom, so its rows differ on every run: it is System.Random seeded from a
// Random without a fixed seed (the exact sequence is not reproduced; the test does not depend on it), with
// DisableDiscoveryEnumeration so that discovery does not fix one random row set. SplittableRandom.nextBoolean() is
// Next(2) == 0, nextInt(bound) is Next(bound) and nextInt(lower, upper) is Next(lower, upper). The anonymous HttpMethod
// subclasses that override asciiName() are the private nested OverriddenHttpMethod (HttpMethod.AsciiName() is virtual,
// as the non-final Java method is). IllegalArgumentException is ArgumentException; String.valueOf(int) is the invariant
// int text. The package-private test methods are public, as xUnit requires.
public class DefaultHttpRequestTest
{
    public static IEnumerable<object[]> InvalidUris()
    {
        return Rows(
                "http://localhost/\r\n",
                "/r\r\n?q=1",
                "http://localhost/\r\n?q=1",
                "/r\r\n/?q=1",
                "http://localhost/\r\n/?q=1",
                "/r\r\n",
                "http://localhost/ HTTP/1.1\r\n\r\nPOST /p HTTP/1.1\r\n\r\n",
                "/r HTTP/1.1\r\n\r\nPOST /p HTTP/1.1\r\n\r\n",
                "/ path",
                "/path ",
                " /path",
                "http://localhost/ ",
                " http://localhost/",
                "http://local host/"
        );
    }

    public static IEnumerable<object[]> InvalidMethods()
    {
        return Rows(
                "GET ",
                " GET",
                "G ET",
                " GET ",
                "GET\r",
                "GET\n",
                "GET\r\n",
                "GE\rT",
                "GE\nT",
                "GE\r\nT",
                "\rGET",
                "\nGET",
                "\r\nGET",
                " \r\nGET",
                "\r \nGET",
                "\r\n GET",
                "\r\nGET ",
                "\nGET ",
                "\rGET ",
                "\r GET",
                " \rGET",
                "\nGET ",
                "\n GET",
                " \nGET",
                "GET \n",
                "GET \r",
                " GET\r",
                " GET\r",
                "GET \n",
                " GET\n",
                " GET\n",
                "GE\nT ",
                "GE\rT ",
                " GE\rT",
                " GE\rT",
                "GE\nT ",
                " GE\nT",
                " GE\nT"
        );
    }

    // CLR: Stream.of(...) of the MethodSource methods as MemberData rows.
    private static IEnumerable<object[]> Rows(params string[] values)
    {
        return values.Select(v => new object[] { v });
    }

    public static IEnumerable<object[]> ValidUris()
    {
        string pdigit = "123456789";
        string digit = '0' + pdigit;
        string digitcolon = digit + ':';
        string alpha = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        string alphanum = alpha + digit;
        string alphanumdot = alphanum + '.';
        string unreserved = alphanumdot + "-_~";
        string subdelims = "$&%=!+,;'()";
        string userinfochars = unreserved + subdelims + ':';
        string pathchars = unreserved + '/';
        string querychars = pathchars + subdelims + '?';
        Random seeds = new Random();
        return Enumerable.Range(0, 1000).Select(_ => seeds.Next())
                .Select(seed =>
                {
                    Random rng = new Random(seed);
                    string start;
                    string path;
                    string query;
                    string fragment;

                    if (rng.Next(2) == 0)
                    {
                        string scheme = rng.Next(2) == 0 ? "http://" : "HTTP://";
                        string userinfo = rng.Next(2) == 0 ? "" : Pick(rng, userinfochars, 1, 8) + '@';
                        string host;
                        string port;

                        switch (rng.Next(3))
                        {
                            case 0:
                                host = Pick(rng, alphanum, 1, 1) + Pick(rng, alphanumdot, 1, 5);
                                break;
                            case 1:
                                host = Pick(rng, pdigit, 1, 1) + Pick(rng, digit, 0, 2) + '.' +
                                        Pick(rng, pdigit, 1, 1) + Pick(rng, digit, 0, 2) + '.' +
                                        Pick(rng, pdigit, 1, 1) + Pick(rng, digit, 0, 2) + '.' +
                                        Pick(rng, pdigit, 1, 1) + Pick(rng, digit, 0, 2);
                                break;
                            default:
                                host = '[' + Pick(rng, digitcolon, 1, 8) + ']';
                                break;
                        }

                        if (rng.Next(2) == 0)
                        {
                            port = ':' + Pick(rng, pdigit, 1, 1) + Pick(rng, digit, 0, 4);
                        }
                        else
                        {
                            port = "";
                        }

                        start = scheme + userinfo + host + port;
                    }
                    else
                    {
                        start = "";
                    }

                    path = '/' + Pick(rng, pathchars, 0, 8);

                    if (rng.Next(2) == 0)
                    {
                        query = '?' + Pick(rng, querychars, 0, 8);
                    }
                    else
                    {
                        query = "";
                    }

                    if (rng.Next(2) == 0)
                    {
                        fragment = '#' + Pick(rng, querychars, 0, 8);
                    }
                    else
                    {
                        fragment = "";
                    }

                    return new object[] { start + path + query + fragment };
                }).ToList();
    }

    public static IEnumerable<object[]> ValidMethods()
    {
        return Rows("GET",
                "POST",
                "PUT",
                "HEAD",
                "DELETE",
                "OPTIONS",
                "CONNECT",
                "TRACE",
                "PATCH",
                "QUERY");
    }

    private static string Pick(Random rng, string cs, int lowerBound, int upperBound)
    {
        int length = rng.Next(lowerBound, upperBound + 1);
        StringBuilder sb = new StringBuilder(length);

        for (int i = 0; i < length; i++)
        {
            sb.Append(cs[rng.Next(cs.Length)]);
        }

        return sb.ToString();
    }

    // CLR: the anonymous HttpMethod("GET") subclasses that override asciiName().
    private sealed class OverriddenHttpMethod : HttpMethod
    {
        private readonly string method;

        internal OverriddenHttpMethod(string method) : base("GET")
        {
            this.method = method;
        }

        public override AsciiString AsciiName()
        {
            return new AsciiString(method);
        }
    }

    [Theory]
    [MemberData(nameof(InvalidUris))]
    public void ConstructorMustRejectIllegalUrisByDefault(string uri)
    {
        Assert.Throws<ArgumentException>(() =>
                new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, uri));
    }

    [Theory]
    [MemberData(nameof(InvalidUris))]
    public void SetUriMustRejectIllegalUrisByDefault(string uri)
    {
        DefaultHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/");
        Assert.Throws<ArgumentException>(() => request.SetUri(uri));
    }

    [Theory]
    [MemberData(nameof(ValidUris), DisableDiscoveryEnumeration = true)]
    public void ConstructorMustAcceptValidUris(string uri)
    {
        new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, uri);
    }

    [Theory]
    [MemberData(nameof(ValidUris), DisableDiscoveryEnumeration = true)]
    public void SetUriMustAcceptValidUris(string uri)
    {
        new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/").SetUri(uri);
    }

    [Theory]
    [MemberData(nameof(InvalidMethods))]
    public void ConstructorMustRejectIllegalHttpMethodByDefault(string method)
    {
        Assert.Throws<ArgumentException>(() => new DefaultHttpRequest(HttpVersion.HTTP_1_0,
                new OverriddenHttpMethod(method), "/"));
    }

    [Theory]
    [MemberData(nameof(InvalidMethods))]
    public void SetMethodMustRejectIllegalHttpMethodByDefault(string method)
    {
        DefaultHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/");
        Assert.Throws<ArgumentException>(() => request.SetMethod(new OverriddenHttpMethod(method)));
    }

    [Theory]
    [MemberData(nameof(ValidMethods))]
    public void ConstructorMustAcceptAllHttpMethods(string method)
    {
        new DefaultHttpRequest(HttpVersion.HTTP_1_0, new OverriddenHttpMethod(method), "/");

        new DefaultHttpRequest(HttpVersion.HTTP_1_0, new HttpMethod(method), "/");
    }

    [Theory]
    [MemberData(nameof(ValidMethods))]
    public void SetMethodMustAcceptAllHttpMethods(string method)
    {
        DefaultHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/");

        request.SetMethod(new OverriddenHttpMethod(method));

        request.SetMethod(new HttpMethod(method));
    }

    [Fact]
    public void TestHeaderRemoval()
    {
        IHttpMessage m = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/");
        HttpHeaders h = m.Headers();

        // Insert sample keys.
        for (int i = 0; i < 1000; i ++)
        {
            h.Set(Of(i.ToString(CultureInfo.InvariantCulture)), AsciiString.EMPTY_STRING);
        }

        // Remove in reversed order.
        for (int i = 999; i >= 0; i --)
        {
            h.Remove(Of(i.ToString(CultureInfo.InvariantCulture)));
        }

        // Check if random access returns nothing.
        for (int i = 0; i < 1000; i ++)
        {
            Assert.Null(h.Get(Of(i.ToString(CultureInfo.InvariantCulture))));
        }

        // Check if sequential access returns nothing.
        Assert.True(h.IsEmpty());
    }
}
