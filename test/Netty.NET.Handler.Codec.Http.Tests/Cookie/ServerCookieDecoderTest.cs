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

using System.Collections.Generic;
using System.Linq;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests.Cookie;

using Netty.NET.Handler.Codec.Http.Cookie;

// CLR: the cookie namespace is imported inside the test namespace (see ClientCookieDecoderTest). Set<Cookie> is
// ISet<ICookie> and List<Cookie> is IList<ICookie>; cookies.iterator().next() is First(). Iterator.next()/hasNext() is
// IEnumerator.MoveNext()/Current: a next() is Assert.True(it.MoveNext()) followed by it.Current, and
// assertFalse(it.hasNext()) is Assert.False(it.MoveNext()). The sorted set orders names with the ordinal UTF-16
// comparison of String.compareTo ("ARPT" < "__utma" < "kw-...", "Session_id" < "session_id").
public class ServerCookieDecoderTest
{
    [Fact]
    public void TestDecodingSingleCookie()
    {
        string cookieString = "myCookie=myValue";
        ISet<ICookie> cookies = ServerCookieDecoder.STRICT.Decode(cookieString);
        Assert.Equal(1, cookies.Count);
        ICookie cookie = cookies.First();
        Assert.NotNull(cookie);
        Assert.Equal("myValue", cookie.Value());
    }

    [Fact]
    public void TestDecodingMultipleCookies()
    {
        string c1 = "myCookie=myValue;";
        string c2 = "myCookie2=myValue2;";
        string c3 = "myCookie3=myValue3;";

        ISet<ICookie> cookies = ServerCookieDecoder.STRICT.Decode(c1 + c2 + c3);
        Assert.Equal(3, cookies.Count);
        using IEnumerator<ICookie> it = cookies.GetEnumerator();
        Assert.True(it.MoveNext());
        ICookie cookie = it.Current;
        Assert.NotNull(cookie);
        Assert.Equal("myValue", cookie.Value());
        Assert.True(it.MoveNext());
        cookie = it.Current;
        Assert.NotNull(cookie);
        Assert.Equal("myValue2", cookie.Value());
        Assert.True(it.MoveNext());
        cookie = it.Current;
        Assert.NotNull(cookie);
        Assert.Equal("myValue3", cookie.Value());
    }

    [Fact]
    public void TestDecodingAllMultipleCookies()
    {
        string c1 = "myCookie=myValue;";
        string c2 = "myCookie=myValue2;";
        string c3 = "myCookie=myValue3;";

        IList<ICookie> cookies = ServerCookieDecoder.STRICT.DecodeAll(c1 + c2 + c3);
        Assert.Equal(3, cookies.Count);
        using IEnumerator<ICookie> it = cookies.GetEnumerator();
        Assert.True(it.MoveNext());
        ICookie cookie = it.Current;
        Assert.NotNull(cookie);
        Assert.Equal("myValue", cookie.Value());
        Assert.True(it.MoveNext());
        cookie = it.Current;
        Assert.NotNull(cookie);
        Assert.Equal("myValue2", cookie.Value());
        Assert.True(it.MoveNext());
        cookie = it.Current;
        Assert.NotNull(cookie);
        Assert.Equal("myValue3", cookie.Value());
    }

    [Fact]
    public void TestDecodingGoogleAnalyticsCookie()
    {
        string source =
            "ARPT=LWUKQPSWRTUN04CKKJI; " +
            "kw-2E343B92-B097-442c-BFA5-BE371E0325A2=unfinished_furniture; " +
            "__utma=48461872.1094088325.1258140131.1258140131.1258140131.1; " +
            "__utmb=48461872.13.10.1258140131; __utmc=48461872; " +
            "__utmz=48461872.1258140131.1.1.utmcsr=overstock.com|utmccn=(referral)|" +
                    "utmcmd=referral|utmcct=/Home-Garden/Furniture/Clearance/clearance/32/dept.html";
        ISet<ICookie> cookies = ServerCookieDecoder.STRICT.Decode(source);
        using IEnumerator<ICookie> it = cookies.GetEnumerator();
        ICookie c;

        Assert.True(it.MoveNext());
        c = it.Current;
        Assert.Equal("ARPT", c.Name());
        Assert.Equal("LWUKQPSWRTUN04CKKJI", c.Value());

        Assert.True(it.MoveNext());
        c = it.Current;
        Assert.Equal("__utma", c.Name());
        Assert.Equal("48461872.1094088325.1258140131.1258140131.1258140131.1", c.Value());

        Assert.True(it.MoveNext());
        c = it.Current;
        Assert.Equal("__utmb", c.Name());
        Assert.Equal("48461872.13.10.1258140131", c.Value());

        Assert.True(it.MoveNext());
        c = it.Current;
        Assert.Equal("__utmc", c.Name());
        Assert.Equal("48461872", c.Value());

        Assert.True(it.MoveNext());
        c = it.Current;
        Assert.Equal("__utmz", c.Name());
        Assert.Equal("48461872.1258140131.1.1.utmcsr=overstock.com|" +
                "utmccn=(referral)|utmcmd=referral|utmcct=/Home-Garden/Furniture/Clearance/clearance/32/dept.html",
                c.Value());

        Assert.True(it.MoveNext());
        c = it.Current;
        Assert.Equal("kw-2E343B92-B097-442c-BFA5-BE371E0325A2", c.Name());
        Assert.Equal("unfinished_furniture", c.Value());

        Assert.False(it.MoveNext());
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

        ISet<ICookie> cookies = ServerCookieDecoder.STRICT.Decode("bh=\"" + longValue + "\";");
        Assert.Equal(1, cookies.Count);
        ICookie c = cookies.First();
        Assert.Equal("bh", c.Name());
        Assert.Equal(longValue, c.Value());
    }

    [Fact]
    public void TestDecodingOldRFC2965Cookies()
    {
        string source = "$Version=\"1\"; " +
                "Part_Number1=\"Riding_Rocket_0023\"; $Path=\"/acme/ammo\"; " +
                "Part_Number2=\"Rocket_Launcher_0001\"; $Path=\"/acme\"";

        ISet<ICookie> cookies = ServerCookieDecoder.STRICT.Decode(source);
        using IEnumerator<ICookie> it = cookies.GetEnumerator();
        ICookie c;

        Assert.True(it.MoveNext());
        c = it.Current;
        Assert.Equal("Part_Number1", c.Name());
        Assert.Equal("Riding_Rocket_0023", c.Value());

        Assert.True(it.MoveNext());
        c = it.Current;
        Assert.Equal("Part_Number2", c.Name());
        Assert.Equal("Rocket_Launcher_0001", c.Value());

        Assert.False(it.MoveNext());
    }

    [Fact]
    public void TestRejectCookieValueWithSemicolon()
    {
        ISet<ICookie> cookies = ServerCookieDecoder.STRICT.Decode("name=\"foo;bar\";");
        Assert.True(cookies.Count == 0);
    }

    [Fact]
    public void TestCaseSensitiveNames()
    {
        ISet<ICookie> cookies = ServerCookieDecoder.STRICT.Decode("session_id=a; Session_id=b;");
        using IEnumerator<ICookie> it = cookies.GetEnumerator();
        ICookie c;

        Assert.True(it.MoveNext());
        c = it.Current;
        Assert.Equal("Session_id", c.Name());
        Assert.Equal("b", c.Value());

        Assert.True(it.MoveNext());
        c = it.Current;
        Assert.Equal("session_id", c.Name());
        Assert.Equal("a", c.Value());

        Assert.False(it.MoveNext());
    }
}
