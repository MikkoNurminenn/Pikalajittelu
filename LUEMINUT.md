# Pikalajittelu 2.0.1-rc1 — Beta

**Vähemmän lajittelua. Enemmän seikkailua.**

**[Tarjoa kahvi tekijälle](https://ko-fi.com/ravimies)** — tuki on vapaaehtoista, ja koko modi pysyy ilmaisena kaikille.

Valheimin repun ja lähiarkkujen järjestelymodi. **Modi on betassa: kehitys ja testaus ovat kesken, eikä tämä ole vakaa julkaisu.**

Nykyinen versio on 2.0.1-rc1. Testauksessa
kahden paikallisen peliprosessin katkostestit läpäistiin, mutta eri koneiden
Steam/crossplay-yhteys ja oikean hiiren/näppäimistön koko toimintaketju ovat
vielä varmentamatta. Tarkemmat tulokset: [TESTIT.md](TESTIT.md).

## Avaaminen pelissä

| Näppäin | Toiminto |
| --- | --- |
| **P** | Esikatsele tavaroiden talletus repusta lähiarkkuihin. |
| **Vasen Shift + P** | Esikatsele lähiarkkujen yhteinen järjestely. |
| **Vasen Alt + P** | Avaa tavarahaku, arkkusäännöt, asetukset ja historia. |

Sulje muut pelivalikot ensin. Näppäimet voi vaihtaa modin asetuksista.
Modin valikon aikana näppäimet ja hiiri kuuluvat valikolle: hahmon liike,
hyökkäykset, kameran hiiriohjaus ja pelin pikanäppäimet estetään. Sulkemisen
painallus kulutetaan ennen pelin ohjauksen palauttamista.
Tavallinen pelin inventaario aukeaa edelleen Tabilla. Modi latautuu pelin
käynnistyessä; asennuksen jälkeen jo käynnissä oleva peli pitää käynnistää uudelleen.

## Asennus

1. Lataa [Releases-sivulta](https://github.com/MikkoNurminenn/Pikalajittelu/releases)
   varsinainen `Pikalajittelu-2.0.1-rc1.zip`, ei GitHubin Source code -pakettia.
2. Pura ZIP kokonaan ja sulje Valheim sekä mahdollinen paikallinen palvelin.
3. Avaa paketin `Asenna.cmd`.
4. Käynnistä Valheim Steamista ja kokeile ensin erillisessä testimaailmassa.

Muu asennuspolku PowerShellissä:

```powershell
./Asenna.ps1 -GamePath 'D:\SteamLibrary\steamapps\common\Valheim'
```

`-ValidateOnly` tarkistaa paketin asentamatta sitä. Asentaja tarkistaa DLL:n
SHA256:n, estää kaksoisasennuksen ja varmuuskopioi aiemman DLL:n paketin
`work/backup-*`-kansioon. Nykyinen BepInEx 5 säilyy; sen puuttuessa ladataan
tarkistussummalla varmennettu BepInEx-paketti. Poisto: sulje peli ja poista vain
`BepInEx/plugins/Pikalajittelu`.

## Käyttö

Esikatselu ei siirrä tavaroita. Vahvistus varaa arkut uudelleen ja tarkistaa niiden
sisällön. Muuttunut suunnitelma näytetään uudelleen hyväksyttäväksi. Sopimattomat
tai tilaan mahtumattomat tavarat jäävät reppuun.

Pikapalkki, varusteet, työkalut, aseet, ruoka, ammukset, juomat ja tehtäväesineet
ovat oletuksena suojattuja. Voit määrittää lisäsuojauksia sekä mukaan jätettävät
määrät, esimerkiksi `Wood=20`. Arkkujen järjestely suosii arkkua, jossa tavaraa
on jo eniten, ja kunnioittaa arkkukohtaisia sääntöjä. Samannimisten esineiden
lisätietojen täytyy täsmätä ennen pinojen yhdistämistä.

Säännöt koskevat tämän hahmon lajittelua tässä maailmassa. Ne eivät lukitse
arkkuja muilta pelaajilta. Isännän asennus ei lisää kaverille omia lajittelunäppäimiä.

Historia näyttää siirrot. Viimeisen tämän pelikerran siirron voi perua vain, jos
kaikki alkuperäiset inventaariot ovat yhä täsmälleen siirron jälkeisessä tilassa
ja käytettävissä. Pelin uudelleenkäynnistyksen jälkeen vanhaa siirtoa ei voi perua.
Ennen/jälkeen-kopiot tallentuvat `BepInEx/config/Pikalajittelu/transactions`-kansioon;
ne eivät korvaa maailman ja hahmon normaaleja varmuuskopioita.

Virheen jälkeen älä toista siirtoa sokkona. Säilytä tapahtumakansio ja katso
`BepInEx/LogOutput.log`. Erillinen vanha hautapalautus on oletuksena pois käytöstä,
eikä sitä ole varmennettu tämän version siirtomallilla. Pidä se pois päältä.

## Saavutukset

Modit ja devcommands voivat vaikuttaa saavutuksiin. Valheimin virallinen
Steam-version uudelleensallintakomento on `yesiuseddevcommandsbutiwantmyachievementsanyway`.
Katso [pelin oma ohje](https://www.valheimgame.com/news/hotfix-1-0-10-1-0-12/).
Modi ei muuta tätä valintaa automaattisesti.

Epävirallinen yhteisömodi. Ei yhteyttä Iron Gateen.
