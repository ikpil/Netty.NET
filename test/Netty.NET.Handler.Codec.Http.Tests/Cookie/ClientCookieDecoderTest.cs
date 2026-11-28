/*
 * Copyright 2014 The Netty Project
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
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests.Cookie;

using Netty.NET.Handler.Codec.Http.Cookie;
using SameSite = Netty.NET.Handler.Codec.Http.Cookie.CookieHeaderNames.SameSite;

// CLR: the cookie namespace is imported inside the test namespace, so that the cookie types are found before the
// deprecated Netty.NET.Handler.Codec.Http.ClientCookieEncoder of an enclosing namespace. Cookie is ICookie. java.util.Date
// is DateTimeOffset (codec-base DateFormatter): new Date(System.currentTimeMillis() + 50000) is
// DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 50000). The UTC Calendar of
// testDecodingLongDates is the DateTimeOffset 9999-12-31T23:59:59Z: Calendar.getInstance() keeps the current millisecond
// in the field that set(...) leaves alone, the DateTimeOffset has 0 ms, and the test's tolerance of 2 seconds covers
// that difference. assertInstanceOf is Assert.IsAssignableFrom. Iterator.next()/hasNext() is
// IEnumerator.MoveNext()/Current: a next() is Assert.True(it.MoveNext()) followed by it.Current, and
// assertFalse(it.hasNext()) is Assert.False(it.MoveNext()). The JUnit asserts with messages use XunitAssertMessages.AssertM.
public class ClientCookieDecoderTest
{
    [Fact]
    public void TestDecodingSingleCookieV0()
    {
        string cookieString = "myCookie=myValue;expires="
                + DateFormatter.Format(DateTimeOffset.FromUnixTimeMilliseconds(
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 50000))
                + ";path=/apathsomewhere;domain=.adomainsomewhere;secure;SameSite=None;Partitioned";

        ICookie cookie = ClientCookieDecoder.STRICT.Decode(cookieString);
        Assert.NotNull(cookie);
        Assert.Equal("myValue", cookie.Value());
        Assert.Equal(".adomainsomewhere", cookie.Domain());
        Assert.NotEqual(long.MinValue, cookie.MaxAge(),
                "maxAge should be defined when parsing cookie " + cookieString);
        Assert.True(cookie.MaxAge() >= 40 && cookie.MaxAge() <= 60,
                "maxAge should be about 50ms when parsing cookie " + cookieString);
        Assert.Equal("/apathsomewhere", cookie.Path());
        Assert.True(cookie.IsSecure());

        Assert.IsAssignableFrom<DefaultCookie>(cookie);
        DefaultCookie c = (DefaultCookie) cookie;
        Assert.Equal(SameSite.None, c.SameSite());
        Assert.True(c.IsPartitioned());
    }

    [Fact]
    public void TestDecodingSingleCookieV0ExtraParamsIgnored()
    {
        string cookieString = "myCookie=myValue;max-age=50;path=/apathsomewhere;" +
                "domain=.adomainsomewhere;secure;comment=this is a comment;version=0;" +
                "commentURL=http://aurl.com;port=\"80,8080\";discard;";
        ICookie cookie = ClientCookieDecoder.STRICT.Decode(cookieString);
        Assert.NotNull(cookie);
        Assert.Equal("myValue", cookie.Value());
        Assert.Equal(".adomainsomewhere", cookie.Domain());
        Assert.Equal(50, cookie.MaxAge());
        Assert.Equal("/apathsomewhere", cookie.Path());
        Assert.True(cookie.IsSecure());
    }

    [Fact]
    public void TestDecodingSingleCookieV1()
    {
        string cookieString = "myCookie=myValue;max-age=50;path=/apathsomewhere;domain=.adomainsomewhere"
                + ";secure;comment=this is a comment;version=1;";
        ICookie cookie = ClientCookieDecoder.STRICT.Decode(cookieString);
        Assert.Equal("myValue", cookie.Value());
        Assert.NotNull(cookie);
        Assert.Equal(".adomainsomewhere", cookie.Domain());
        Assert.Equal(50, cookie.MaxAge());
        Assert.Equal("/apathsomewhere", cookie.Path());
        Assert.True(cookie.IsSecure());
    }

    [Fact]
    public void TestDecodingSingleCookieV1ExtraParamsIgnored()
    {
        string cookieString = "myCookie=myValue;max-age=50;path=/apathsomewhere;"
                + "domain=.adomainsomewhere;secure;comment=this is a comment;version=1;"
                + "commentURL=http://aurl.com;port='80,8080';discard;";
        ICookie cookie = ClientCookieDecoder.STRICT.Decode(cookieString);
        Assert.NotNull(cookie);
        Assert.Equal("myValue", cookie.Value());
        Assert.Equal(".adomainsomewhere", cookie.Domain());
        Assert.Equal(50, cookie.MaxAge());
        Assert.Equal("/apathsomewhere", cookie.Path());
        Assert.True(cookie.IsSecure());
    }

    [Fact]
    public void TestDecodingSingleCookieV2()
    {
        string cookieString = "myCookie=myValue;max-age=50;path=/apathsomewhere;"
                + "domain=.adomainsomewhere;secure;comment=this is a comment;version=2;"
                + "commentURL=http://aurl.com;port=\"80,8080\";discard;";
        ICookie cookie = ClientCookieDecoder.STRICT.Decode(cookieString);
        Assert.NotNull(cookie);
        Assert.Equal("myValue", cookie.Value());
        Assert.Equal(".adomainsomewhere", cookie.Domain());
        Assert.Equal(50, cookie.MaxAge());
        Assert.Equal("/apathsomewhere", cookie.Path());
        Assert.True(cookie.IsSecure());
    }

    [Fact]
    public void TestDecodingComplexCookie()
    {
        string c1 = "myCookie=myValue;max-age=50;path=/apathsomewhere;"
                + "domain=.adomainsomewhere;secure;comment=this is a comment;version=2;"
                + "commentURL=\"http://aurl.com\";port='80,8080';discard;";

        ICookie cookie = ClientCookieDecoder.STRICT.Decode(c1);
        Assert.NotNull(cookie);
        Assert.Equal("myValue", cookie.Value());
        Assert.Equal(".adomainsomewhere", cookie.Domain());
        Assert.Equal(50, cookie.MaxAge());
        Assert.Equal("/apathsomewhere", cookie.Path());
        Assert.True(cookie.IsSecure());
    }

    [Fact]
    public void TestDecodingQuotedCookie()
    {
        ICollection<string> sources = new List<string>();
        sources.Add("a=\"\",");
        sources.Add("b=\"1\",");

        ICollection<ICookie> cookies = new List<ICookie>();

        foreach (string source in sources)
        {
            cookies.Add(ClientCookieDecoder.STRICT.Decode(source));
        }

        using IEnumerator<ICookie> it = cookies.GetEnumerator();
        ICookie c;

        Assert.True(it.MoveNext());
        c = it.Current;
        Assert.Equal("a", c.Name());
        Assert.Equal("", c.Value());

        Assert.True(it.MoveNext());
        c = it.Current;
        Assert.Equal("b", c.Name());
        Assert.Equal("1", c.Value());

        Assert.False(it.MoveNext());
    }

    [Fact]
    public void TestDecodingGoogleAnalyticsCookie()
    {
        string source = "ARPT=LWUKQPSWRTUN04CKKJI; "
                + "kw-2E343B92-B097-442c-BFA5-BE371E0325A2=unfinished furniture; "
                + "__utma=48461872.1094088325.1258140131.1258140131.1258140131.1; "
                + "__utmb=48461872.13.10.1258140131; __utmc=48461872; "
                + "__utmz=48461872.1258140131.1.1.utmcsr=overstock.com|utmccn=(referral)|"
                + "utmcmd=referral|utmcct=/Home-Garden/Furniture/Clearance,/clearance,/32/dept.html";
        ICookie cookie = ClientCookieDecoder.STRICT.Decode(source);

        Assert.Equal("ARPT", cookie.Name());
        Assert.Equal("LWUKQPSWRTUN04CKKJI", cookie.Value());
    }

    [Fact]
    public void TestDecodingLongDates()
    {
        DateTimeOffset cookieDate = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);
        long expectedMaxAge = (cookieDate.ToUnixTimeMilliseconds() - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000;

        string source = "Format=EU; expires=Fri, 31-Dec-9999 23:59:59 GMT; path=/";

        ICookie cookie = ClientCookieDecoder.STRICT.Decode(source);

        Assert.True(Math.Abs(expectedMaxAge - cookie.MaxAge()) < 2);
    }

    [Fact]
    public void TestDecodingValueWithCommaFails()
    {
        string source = "UserCookie=timeZoneName=(GMT+04:00) Moscow, St. Petersburg, Volgograd&promocode=&region=BE;"
                + " expires=Sat, 01-Dec-2012 10:53:31 GMT; path=/";

        ICookie cookie = ClientCookieDecoder.STRICT.Decode(source);

        Assert.Null(cookie);
    }

    [Fact]
    public void TestDecodingWeirdNames1()
    {
        string src = "path=; expires=Mon, 01-Jan-1990 00:00:00 GMT; path=/; domain=.www.google.com";
        ICookie cookie = ClientCookieDecoder.STRICT.Decode(src);
        Assert.Equal("path", cookie.Name());
        Assert.Equal("", cookie.Value());
        Assert.Equal("/", cookie.Path());
    }

    [Fact]
    public void TestDecodingWeirdNames2()
    {
        string src = "HTTPOnly=";
        ICookie cookie = ClientCookieDecoder.STRICT.Decode(src);
        Assert.Equal("HTTPOnly", cookie.Name());
        Assert.Equal("", cookie.Value());
    }

    [Fact]
    public void TestDecodingValuesWithCommasAndEqualsFails()
    {
        string src = "A=v=1&lg=en-US,it-IT,it&intl=it&np=1;T=z=E";
        ICookie cookie = ClientCookieDecoder.STRICT.Decode(src);
        Assert.Null(cookie);
    }

    [Fact]
    public void TestDecodingInvalidValuesWithCommaAtStart()
    {
        Assert.Null(ClientCookieDecoder.STRICT.Decode(","));
        Assert.Null(ClientCookieDecoder.STRICT.Decode(",a"));
        Assert.Null(ClientCookieDecoder.STRICT.Decode(",a=a"));
    }

    [Fact]
    public void TestDecodingLongValue()
    {
        string longValue =
                "b___$Q__$ha__<NC=MN(F__%#4__<NC=MN(F__2_d____#=IvZB__2_F____'=KqtH__2-9____" +
                "'=IvZM__3f:____$=HbQW__3g'____%=J^wI__3g-____%=J^wI__3g1____$=HbQW__3g2____" +
                "$=HbQW__3g5____%=J^wI__3g9____$=HbQW__3gT____$=HbQW__3gX____#=J^wI__3gY____" +
                "#=J^wI__3gh____$=HbQW__3gj____$=HbQW__3gr____$=HbQW__3gx____#=J^wI__3h_____" +
                "$=HbQW__3h$____#=J^wI__3h'____$=HbQW__3h_____$=HbQW__3h0____%=J^wI__3h1____" +
                "#=J^wI__3h2____$=HbQW__3h4____$=HbQW__3h7____$=HbQW__3h8____%=J^wI__3h:____" +
                "#=J^wI__3h@____%=J^wI__3hB____$=HbQW__3hC____$=HbQW__3hL____$=HbQW__3hQ____" +
                "$=HbQW__3hS____%=J^wI__3hU____$=HbQW__3h[____$=HbQW__3h^____$=HbQW__3hd____" +
                "%=J^wI__3he____%=J^wI__3hf____%=J^wI__3hg____$=HbQW__3hh____%=J^wI__3hi____" +
                "%=J^wI__3hv____$=HbQW__3i/____#=J^wI__3i2____#=J^wI__3i3____%=J^wI__3i4____" +
                "$=HbQW__3i7____$=HbQW__3i8____$=HbQW__3i9____%=J^wI__3i=____#=J^wI__3i>____" +
                "%=J^wI__3iD____$=HbQW__3iF____#=J^wI__3iH____%=J^wI__3iM____%=J^wI__3iS____" +
                "#=J^wI__3iU____%=J^wI__3iZ____#=J^wI__3i]____%=J^wI__3ig____%=J^wI__3ij____" +
                "%=J^wI__3ik____#=J^wI__3il____$=HbQW__3in____%=J^wI__3ip____$=HbQW__3iq____" +
                "$=HbQW__3it____%=J^wI__3ix____#=J^wI__3j_____$=HbQW__3j%____$=HbQW__3j'____" +
                "%=J^wI__3j(____%=J^wI__9mJ____'=KqtH__=SE__<NC=MN(F__?VS__<NC=MN(F__Zw`____" +
                "%=KqtH__j+C__<NC=MN(F__j+M__<NC=MN(F__j+a__<NC=MN(F__j_.__<NC=MN(F__n>M____" +
                "'=KqtH__s1X____$=MMyc__s1_____#=MN#O__ypn____'=KqtH__ypr____'=KqtH_#%h_____" +
                "%=KqtH_#%o_____'=KqtH_#)H6__<NC=MN(F_#*%'____%=KqtH_#+k(____'=KqtH_#-E_____" +
                "'=KqtH_#1)w____'=KqtH_#1)y____'=KqtH_#1*M____#=KqtH_#1*p____'=KqtH_#14Q__<N" +
                "C=MN(F_#14S__<NC=MN(F_#16I__<NC=MN(F_#16N__<NC=MN(F_#16X__<NC=MN(F_#16k__<N" +
                "C=MN(F_#17@__<NC=MN(F_#17A__<NC=MN(F_#1Cq____'=KqtH_#7)_____#=KqtH_#7)b____" +
                "#=KqtH_#7Ww____'=KqtH_#?cQ____'=KqtH_#His____'=KqtH_#Jrh____'=KqtH_#O@M__<N" +
                "C=MN(F_#O@O__<NC=MN(F_#OC6__<NC=MN(F_#Os.____#=KqtH_#YOW____#=H/Li_#Zat____" +
                "'=KqtH_#ZbI____%=KqtH_#Zbc____'=KqtH_#Zbs____%=KqtH_#Zby____'=KqtH_#Zce____" +
                "'=KqtH_#Zdc____%=KqtH_#Zea____'=KqtH_#ZhI____#=KqtH_#ZiD____'=KqtH_#Zis____" +
                "'=KqtH_#Zj0____#=KqtH_#Zj1____'=KqtH_#Zj[____'=KqtH_#Zj]____'=KqtH_#Zj^____" +
                "'=KqtH_#Zjb____'=KqtH_#Zk_____'=KqtH_#Zk6____#=KqtH_#Zk9____%=KqtH_#Zk<____" +
                "'=KqtH_#Zl>____'=KqtH_#]9R____$=H/Lt_#]I6____#=KqtH_#]Z#____%=KqtH_#^*N____" +
                "#=KqtH_#^:m____#=KqtH_#_*_____%=J^wI_#`-7____#=KqtH_#`T>____'=KqtH_#`T?____" +
                "'=KqtH_#`TA____'=KqtH_#`TB____'=KqtH_#`TG____'=KqtH_#`TP____#=KqtH_#`U_____" +
                "'=KqtH_#`U/____'=KqtH_#`U0____#=KqtH_#`U9____'=KqtH_#aEQ____%=KqtH_#b<)____" +
                "'=KqtH_#c9-____%=KqtH_#dxC____%=KqtH_#dxE____%=KqtH_#ev$____'=KqtH_#fBi____" +
                "#=KqtH_#fBj____'=KqtH_#fG)____'=KqtH_#fG+____'=KqtH_#g<d____'=KqtH_#g<e____" +
                "'=KqtH_#g=J____'=KqtH_#gat____#=KqtH_#s`D____#=J_#p_#sg?____#=J_#p_#t<a____" +
                "#=KqtH_#t<c____#=KqtH_#trY____$=JiYj_#vA$____'=KqtH_#xs_____'=KqtH_$$rO____" +
                "#=KqtH_$$rP____#=KqtH_$(_%____'=KqtH_$)]o____%=KqtH_$_@)____'=KqtH_$_k]____" +
                "'=KqtH_$1]+____%=KqtH_$3IO____%=KqtH_$3J#____'=KqtH_$3J.____'=KqtH_$3J:____" +
                "#=KqtH_$3JH____#=KqtH_$3JI____#=KqtH_$3JK____%=KqtH_$3JL____'=KqtH_$3JS____" +
                "'=KqtH_$8+M____#=KqtH_$99d____%=KqtH_$:Lw____#=LK+x_$:N@____#=KqtG_$:NC____" +
                "#=KqtG_$:hW____'=KqtH_$:i[____'=KqtH_$:ih____'=KqtH_$:it____'=KqtH_$:kO____" +
                "'=KqtH_$>*B____'=KqtH_$>hD____+=J^x0_$?lW____'=KqtH_$?ll____'=KqtH_$?lm____" +
                "%=KqtH_$?mi____'=KqtH_$?mx____'=KqtH_$D7]____#=J_#p_$D@T____#=J_#p_$V<g____" +
                "'=KqtH";

        ICookie cookie = ClientCookieDecoder.STRICT.Decode("bh=\"" + longValue
                + "\";");
        Assert.Equal("bh", cookie.Name());
        Assert.Equal(longValue, cookie.Value());
    }

    [Fact]
    public void TestIgnoreEmptyDomain()
    {
        string emptyDomain = "sessionid=OTY4ZDllNTgtYjU3OC00MWRjLTkzMWMtNGUwNzk4MTY0MTUw;Domain=;Path=/";
        ICookie cookie = ClientCookieDecoder.STRICT.Decode(emptyDomain);
        Assert.Null(cookie.Domain());
    }

    [Fact]
    public void TestIgnoreEmptyPath()
    {
        string emptyPath = "sessionid=OTY4ZDllNTgtYjU3OC00MWRjLTkzMWMtNGUwNzk4MTY0MTUw;Domain=;Path=";
        ICookie cookie = ClientCookieDecoder.STRICT.Decode(emptyPath);
        Assert.Null(cookie.Path());
    }
}
