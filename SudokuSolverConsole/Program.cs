using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Mono.Options;
using SudokuSolver;

namespace SudokuSolverConsole
{
    class Program
    {
        public static readonly (string, string)[] uniqueVariantFPuzzles = new (string, string)[]
        {
            // "Clipped" by glum_hippo: Arrow, Thermo, Givens, King
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QGEsIAHYmAExFQEMBXNACwHsAnBEABQYiNIAIAQlloBbGH2oBranwDmwkQH1upJlRAtaOMDDTsAyrXJNJtPgFo+MAG4wWATz4smAd2R8AxkwUA7d9R9yPgBmAA9gvgAjJlC+EVowNE8mHzRqCB8JLCw+RnFyCFlMMD4AM2cRPgBGc0QAOj59JjE+AqK0EuoWcWosbupyRyLbHzqAHR8JgEFUiHMAaQzZCysAR1pe1sLiuOpHHyYktCZaDwYt6lkU3qx7ccmfABUGOxFm3TsVtp34xL5ElgQDzYRwZDz9HRlCpRLSRXJMXIke7TFjOFwrPKeCAsDw4ILfJJeHyJTD0GAlTFgUR8JilXIvLbtTpYFLLTFdNH3dSyQGUeAAbX5wAAvsgRWLReKpZKZRKALrIIWy6US1UqkUKpVq5Ugay9Wi4ABsqGGMB8CDQmhgyptmvVNu12rtOr1wlwAFYTRARharS79bgql6ffBLQb/W6EAAWYNm33hp2K+2O5MapMOqW6gMIYKx82hv0pmXOoul6Ul1MZ4sKkABDCSJbxmCoPIsN5iNB2BBCkBEHzk7v8kAAJUN+Hd6mHAHZ8FHJwAOfDBBf4ABMIDlcslvYyA4FQ+H7rXk6jS8nwVnk9Xl83277e8FI9PE9Qw4vhqv+Cnn/nG63Yp3fswEHEcj1/V8x2/V8Zw/V9Fw/W8aw5Vxu1Ae9gP3UDL1fU9l1fC911fa9103VAPBgbIMIPI853/NDdyo6j8FgkdIMnGdwJHRdf1IkByMo7ssIQu8GJA4dFxfEcZznCCz1kkia34rAqK48cNxEoCxOvSS32Yk8v30njFIo5TBOHbT1IA9CxJnPDVJkkdEDU3ilJU6czy3LcgA=", "867452931342891675519763284984327156635918427721645398498536712276189543153274869"),

            // "Bird in a Storm" by Gustaf Hafvenstein: Palindrome, Kropki w/negative constraint, King, Nonconsecutive
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QCEIAnAEwAIIA7MgQzLDQHsiBbEVGgVzQAtmEQAcU4MaAMzIAJcQDcYlBjCrsQRTjjAw0AgMpoalEjVL1OJRgGtOZNRtoAHe1gCeAOjIAdSl70GjJiyJGewsIG3UYMAcnNzIAQTIAIywaAGMLMnM0ej4Adyi6IhoMRjJGCQBGeAAmJK1cmHkyNFzS1JgsLDB3BNyeTBhMxmzE+sbqFraOrrIWGAMo3hhnWiJB1MYFGFTuCDkezqG0ArWyAHM9+VcvLzjKDABaC0oIM55s20jol3cAOVKSK9MGRUgZ6I0KMcOhIqLQQTxIlFnq93oBMAiiLEY+xulAAKgjMkVctQsFRBhACmR7DRSYYgnNkLRDLMRB95uQlvQaHMyLkaCsxPSksMeGR5CRuiozkQICQEABteXAAC+yBVatV6q1mp1GoAusglbrtRrTSaVQajWbjTbrZbzbaHfrDU7XTb7Y7PXaXV63c6rX63R7rSGgwaQAYMMi3tp4Gg1DBUJRNhstjsMHIEPHOImQNTaSR6bglSBaZEFfKQAAlRAAYQAbCoa7WAKxNgActYALE2AOzdvutpv1htNlu13vDidj0eoKtd6dzheNucAZkX1fXK+r1Vnm6Hc93bcPA7XtdXTfXPaX55nF7nI/v1f7T6r/eqU4qID1euV4cBYhiDAayUO0CqgO0nRgAq1YVBuVZwY2v5qiAkFdDBVa7l+J4fshEHTNB8CVphtaIE2u7tt+mqoQRGHrq+V5UShaGEcR67YfuuHUSxGELq+C5ccxtFEdWfFNguPZ4TRUEYeOk5zuOlFSTxIlVuO25qdOynCcR47XtW45ttpMmqeOHGaYJ+EmcRI7mSOlnSehqkjvJ1Yjkh3E6c+8H9kpnnWd55nvkxVlOcRdaUXOdaTsZYXeR+D61g5KnEZ2r51hesWsfu/G3llGFwa+u6Zf5cVVoxJ6SaV2Xzqe+5VUJAWafptUNaFNWdi1dZtY5HUHtWdZGdVsn9VWI5DY1ZVwceO5Dvlzl7ppHmTTV/Yae5IW9RhdYaZ2y3tSNrljVpw2qRRTZwX5K0Yb5U5XQdql1mRc6dmR802aRg5vadxFwc9s3fddqmFRdA6/uGlAwGcxSXDBRQlN+qDwxAjAKr+QA===", "317649825986257134452381796623418579879562341541973268238194657795826413164735982"),

            // "Blitz" by Patrick: Killer (with and without sums), Little Killer, Even, Odd
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QCEtMoRUBDAVzQAsB7AJwRAAUy16IBjAa1JHoo4wMNEwByDALZksAAgDKFACa1uFZLIDSELDnqyAwmQDmMMBoAymbDC069GgPJKlGgKIA3GADsAdH2MOJQQAbRDgAF9kSOiomPi4xNiAXWRwpITYrMzI1PTsjMKCvJyi0pS08qrCkrK64sr66or85urags721JBaF1DQThhdJgAlADYDcZA4kCGRvFGAJgMAZhnoueGsMcQDRBmemC9vAa2FkFGAFgMljcHtsYBGAwBWQ9Ruexh6ThNccLnLBgUKXFbrVCjVZrPjXW6wm4Qy6veGQyZPWGTO6QgDsBgxkIAHPiQMlZvNgaDllNYdDppCbu8GTTISimZdJldMW9YXiuUSDFyyZsKSD4CEwftaVLmYSETLkQY5WiDDjucrLni1QK1cKHroxRLRiiDirTZqFaNifTLsTzaM9jaHarYXsNc6DnqgYbLi8Cb7UQGkaMXvzJf7qWGoSTmRGURivaKqS92SGWQHtZLUytM9Go9DU9DczcheTtj6JunRnjg3jU3incTg8TU3tg3so3t3ony1S9hG9nceh4ZBRcCA1WWDVS8djbfDh6Px08uVPKeLJXPo0PUCOsGOmE9pmuK3j3cS5Yv9+PVyLexvjTDWYLuesrwe8E85SeqTdcyinRRXVdyXJhvzvacHxed0Xk9EDr0PDEfwfFZ3WhS94I/EAnjuCIeiINAbC+XQfjAChJDOCkxgABhfVAkygp9N2lBNUCUCB6BgTgMFoU48AAEQsPg9ywp5JwgnZFlom0GKNF5c1Q6U4JAdjOO4iBeKYfjRmE0DP3A/VJMuG5qL4WTFQjLFeSYq06MuLtSTYjiuJ4viQG03SELwJZ1jwiIgA", "132748956475916283896352741528637194619284375347195628781469532964523817253871469"),

            // "Self-contained" by Lavaloid (killer, knight)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QGUYsAzAWgGMB7AOzQEMJqYATEVOgVzQAtKAnBCAAydAG50slCK1R8OOMDDSCA1AAIAcvwC2EtWA7NKAaw5q5CtXQAO1rAE8AdAB1qAUVEw+985QDuyGpUWBza1IF01MxqAMwAHjFqAEaUcUE09IxgVlhYaswQAOaY2cR8lNpqAIxqaJRqiC7UruoAgrQQxtRF3Gjm8jBWtlgQMGBNACJFJfow1nR8dGgsyT50Qdxj2V09aADk2dqUnmq6PtSUfUmD0jAd5BJNLWoA0hC5Xv04Q3aj465TYpobKMKxBOiFQbaDhgPoGSp1WqbNTUULXPhqPx8TDLahqUE8QZ1axqHDENCONSAmbQ2Eoy7mOYwJb4vHrB6QxxsECFbGseAAbQFwAAvshReKxRLpVLZZKALrIYVymWStWq0WK5XqkDkQQAYgmAAZjQAxU0gKW6g3Gs0Wq16vCGk1G82W8XWp22132j2OkDOu3umVajWegPet0Om0uqN+mNB6Ne2O+0D+wM+4Oy0Mq8MZuNphOZpMRlPBvOR1MVssqkU5nXpyvlxs1+PJxNt0sdwvt4vqutKsMt7vVkfDvs9rsT0cT7OD2v9xch+dLhcauuKkCRDA7Qq9BBoOQwVDGd44Pgc3DC3VELBgBACkAAJQAbABhGLcp8Adg/X9/AAsICbuIIS4CAABMn4Ore96Cs+AF/qgT6IUByGIQArMBqCgRw4FQc2sEPs+MRISRb5ASBEh4YIAAchG5HBj5PhhZEsRRX7vpROHUeBVRYTBjHEexWHIe+onPu+L7YSAuF8YgDF3sJiHfl+rHSchrGqVRYGCFUQGCUp8FPqREkmW+Gnkap6EWTJcl6QJfpEcZv5mb+0k6TReD0YZTGSW+1nPr+2k8bpeDSXWIpAA===", "653418279174259863982673451528961347317542986496387512769835124831724695245196738"),

            // "Killer Blister" by Rangsk (Killer, Little Killer)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QGkIscAnAAgCEsIw0YSRUBDAVzQAsB7BvAJSYB2AczABrRiBIscYGGgQgActwC2TLGQDKLACadRLMlJlkmABzNYAngDoAOgIea0gnUxI6yhYvXhkAKuwwZDoQQphgZBACpmQAxkxCwWAsKmRonOlB8VgswdFZwRlmZDgAZmhknGWF8YkwNmQAImERdQICnJUkMGYwTJUFTHVJ9o4Czq7ungAymNjB3qR+AMK5MJGcbJA6RdlCJBCe0aEJdLUpadW1oeFokeqcwrVmnNF0nqGJT+o2EgdHBAAbSBwAAvshwZCIVDYTD4dCALrIUEIuHQjHo8HI1GYtH4vE4rEE4lIlGkin4okkmmE8m0ylk3GMynUvHs1nIkCiIikBJJYGgOIwYhgYEgXgAZhWAEYJLwACyykBcgBu6jyCgA7CAYSBhaLxYqVgAmeVKyUq1Dq9YKS16g1YMXwIESgCsKwV8o9bu9KwAbFaQDbNXgdQ6RU6jf6VjrULwYwAOeUxxDyrUrNNqjW4EAygAMush+sjztdvBNK0t8elZprVaDIdzMuTEcNLolMbl8Yz3YlieV2dteBNhbbUY7vAzcYlGeT8YHM94A+TQ9DebN47LEplK198cr+4l0qPxt9a+bga30b3KYD6dvPfvC8f/fvF7treLju3y9N8oHas3y9D88BlcNv1LI1d0DeNdyXXd5wlSsdVAkATSvSD23LaVYOPWN5WlJCpUzRscwUGVNy5ag0AWHkfBIS5BRLYgFGNQtUB/I1pT7Ct/zghsuVCHo4gwJ4FAAVV4CQmwUUciyFSM2IHAsJC4ycMzTeMkz9JclTwkjT0rL0BMtISIBEsSBEkmYZPIvBJQgxTWL4AtX3U8tEE9ACGyfOtO0HVBhJgUSIHEvAJNs617I3BSWKwNi3I4+Lfy83iB38qdfICkz3VfY0DOlJdK2I3csyCiyQqsyTpOi4cQDdL0wURMEgA", "249516837185347962376982154421639785758421396693875421917268543564793218832154679"),

            // "300 Subs!" by Rangsk (Thermo, Kropki w/no negative constraint)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QGYAGQgAgGUBXAIzAEIRUBDCtACwHsAnBEAJUYB2AczABrBiE4UcYGGh4A5LgFtGWchQAm7URRJSZJRgAdjWAJ4A6ADoDbZNIM2NOmkmxidl7ZXM9h4EgARCCFMMBIwNE4IAGNscxIIAVjOGEZZEgAzTh93VhgSKmkqEhgBTRs7AQcnFzcAaVzjUQgSAAoBdhIBGCFGDAA3Qtj2ASjORmS0AEpAkLC0CNljFwGYNypExhIAd1ZMQu00IzSSUfGYWJYIYctg0PDImFXJtA2i7aKsRljREmOJFYjGGRhIAEZ4AAmfQDCDsSwSIQxTQIADaaOAAF9kNjcTi8YSCcT8QBdZCYklE/E06nY8mU2lU5lMhl0lnsskUzk85lsjkC1ncwW8rmM0W8/lM6WS8noApeHx+bjwTEgLDJGBgdFovj4ADCABYJLxDUaTQBWc2oXhWi2W/UANhNZudNoNbr1+vtpNJBPVmu1qt1tv1AHYTY7wyaw9GbbGABwx/WISMph2pm1WpO+/0a3pBjF8cH68EmqH6qEmg34F2Vh21m1Rqvx0sgXNyzQQLJZTzlWK4NUDrBYQt8RDWvgJ81+3EgYej9FT70mic+/0Lse8acRm0TiOz0Cbpe8Cu7vgV52H+cwEdbqP2pszrFyt7w9FH2+L1UXye8EvGtex4/tu+pJja06pkBX5btOLbLrW0F3ieJbnqe0ZId+IbTsaEErn6fpAA=", "135462879729853461684197523851276934296345187473981256962718345548639712317524698"),

            // "Black Kropki X" by Florian Wortmann (Diagonal +/-, ratio with negative constraint, little killer)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QCEsBDAYwGsACAaQCcB7ABzIgoA0RUiBXNACzpoIQAMSwCIRAHYUA6gLQBbKZI4gaXHGBhohAOQFKsFAMpcAJnTJcK6zRSlmKAIzp8KSiNLMSA5nUlEWGAUJP5oRJ4UAIwAtIj2wQDuMFhYAHQAOpJZhKSUFmjBnt4kRGgw9jZlEHQUdABm0fAATBSRfBWhkpIwJOWOJClBaRQAgqkUDHRgkE44zsTkFAXBRDQVPhAAbjCSmdmSADKY2BVUEKkwNCFYXDDBYPyJFB0UYFwKdY2v3puFddJXsUIKV+stfP5AmlVD4aBAzAgANqI4AAX2QaIx6MxOOxeKxAF1kCj8biseSyWiiSSKaS6bTqZT6UzCcSWey6YzmdyGWyeRzWTSBRyubSxSKiSBvEQ/AEsABqBBodQwVDS2WBGJKlWoLAnHDMS40d4KJGgQapIQAJQA7ABhAAMqgtQSRICtAA47VFVFbEHbmiBJd51n0aio8AARK2qLaBO5CG0gbEgF3W+1RJ2oF1gN2eu2IX3+j1BtUQUMYfxCSOHWPx3AgJMptN4K0Ou0AVmdQ1z8ER7qidoAbL7mnak6grQBmO0lycAFgLpal5d6lYjIGjdduDeahdRkp6PmqOzdNGqdGX58rZtTPbzM6nvpngYJzfvffdM4nX+HQffqS9v2VqLk+C4Bv+GJ3oBeZDna86+nBXZvlBOZ5vahaTvaJYoeaH7AV6YHul6r4Aa6n75iOk5eshZFAe6/oIZO/pPrh0HkcB/pdsx8GQXhMEUfaP5WnBSZsWhFGLoGk4dhB4n4e6i7Cd+fHsfRVqDnO7pjjhdF5oOPqTmOPryQJwFwVR7pIapEnAYuhnurJJl6RRsnSVZckucBGG+l6hamRxinjr6sliV5jlLjJs42QpVpjphA5LgF6lwVpInBW+b5AA", "472596813895431726136728495348169257629357184517842639981675342263984571754213968"),

            // "Tangled Arrows" by Madison Silver (Diagonal +, odd, even, palindrome, clone, arrow)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QBUBDAOwHMcATAAgEEAnOgewHcwRVCBXNAC0boQgAsoQoQwjYlQDKELADcYA1HU44wMNIIC0AOX4BbQlhmcKjANacqAJTUwwVQgAdnWAJ4AdYtoDCMLCxHZkweKlI6GHcqMABHTkJIxwNOMDQqAGNJNEIIKRhFKTFSTDAAOm8/AKCqEN5wyOiMiDoM9SoUtMzs3KlGCmpi0oqfABEIErRHY0lSJylEpmYYzgMqNEZ1nhgqYlWAIyUqPK2drOJiGAy0GGpm1pwR7XHJ5MJo4kZ0yOcYQnSZmRTsdiGIMv9blQxIRSJJjE8AOKNKhYPIOJyRKjOYx5ChMAzogAUGniMGIGXRjAAZlCJqVajwIBkwokdpFRMCwIQCVR9oQMhZmIlqIRHFT+EK6BQygBKSr4bbrZibfZYThsmAlSTTTFtSQOMrsEARCAUBAAbXNwAAvsgbXbbfanY6XQ6ALrIK2u50O30+m0er1+kAZQQAYlGAAYowAxGMgR0h8NR2Px73p92e/1JvAR6ORuMJu05kB51NF/3pwOVv0Z53Vuvektlgtp4uh3Mp1sVl0N2v90Ad0tdwuJoct0e1vs1me9rONgdVj0gaGw4jGADUCDQqhgqH6ZvgVpD1UENgALL5zxWT4Ez5eAKw3il3vA2ABsvifY9Pb4ATL4f7Pr+IA2AAHL4YHAa+oEAOyAdBWBngAzJBCbLgUZIWoOIEfr476IWeiBoT+ME2AAjAh1rLtiqKgviuDHnRDgWuacG+MhRp4ZxqA2A+V4gG6bqJsxbBHmxNiobBXGXtJvH8QRQkiWiYmWqBn5Qbxn5yexmnsdJSnFqJrGgahPGmQhvGXuZF4IYZoDGeJoEAUBvGoeRMm+B59kgI5angWhvHwYgXGfiFSnLnqlzYbeQQWhZNmodeVkcZ516RVg+r+IEqnqfhoW+DpNjwQRQWFYJ1EcAwLAxX5EmfsloH8Y1tnpZF1S5Xh6XKZcqkSZepVNflWn5UJqAvnFR6gQNFVGSpJkUcNzlLZJo3tTl8WLYpPUsU5NjES1EEtfBbXjR1m0HbNDnzXt8E2RBrmgcR3nrZNEl3Vdvk3f5UlcQBemLeFr2db9wlzb1C38TZ1meUBY2xZ1UOfXVQ1FdpBVQfDE2I+VwnCUAA===", "549328761678941235132567498723156984815794623964832517497283156356419872281675349"),

            // "XV Kropki X" by Florian Wortmann (Diagonal +/-, kropki no negative, XV with negative)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QA0A1AAgGkAnAewAcBrCY/EVAQwFc0ALS8hEAMSw8ILAHbEA6jzQBbMaOYhybHGBho+AOR5ysxAMpsAJpVptiy1cTFHiXGHYDulYnIjijIgOaVRLLGDEAMa+aCzuxACMALSI1oGOMFhYAHQAOqIZ2uS6ZFR0DJYwYPAZjpyYDgC8wb5qQRwQAG4wGQBGWCxBtMQ1kfAATBYsGJRZlGjWycTUlGCQHQ4maIEs5A5ezTCi6ZmiAMJJAcRq1DDkIzBGGdHEbQCe1uL4J2wydi6RAAw3d48sxBIYDeH2IAFYMgBBaazeYQRaMVaiWyEVbrYibFo7RRecgQIwIADahOAAF9kGSKeTKTTqXSqQBdZAk+m0qnstlkpksjmsvm87mc/lCxnMkXivmC4XSgVimUS0U8hUSqW8tUqpkgTwsHx+LAAagQaGUMFQ2t1/miRpNZogADM7edtkFcCSQC7kmAiSAAEoAZn2fsUPoGgZADOp7qOXvghN9oYALMHQ0GIxSo57vT6ABz7ABswdzAHZw5GPQEs7mC6gfYh86X0+WY3GfUX6zWqw3QE2s23s8G2yW093o1m8/t+zW+12MxXY77cwNg3Wl8PZ83fXWgzWVzOe/Oc2Gd2G1/uW6Gwcn9kmI5qLqMiSPMweA9vfQHV2XRwfOx39pfT2/FtB2Dcchy/Z8W3HSdfTAvcgIXI9EM/TVRBgLwRi2b0AA8mnDVBcMfdcswDS8awvfCQCafw2FwEBCBACC5xbUjgwDAtNWorBaL4JhSQjIA===", "472596813895431726136728495348169257629357184517842639981675342263984571754213968"),

            // "Boxes" by Clover (Killer, Quadruple)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QCEB7ADxjBFQEMBXNAC0ICcEQBjLQgNxkYEIKQjajjAw0LAMppKAOwAmlRnIAEYanMIBrasqEjllAA6GsATwB0ygDoybAEQgBzTGGUQZB5a0qOYygLaUpsoyhGi6MIYwlOGyKv7UYOFq/spohGl0fpyUWNR+7nIQ3mgwKgAUEABmyoaMXBByZQCUljb2Ti5uHpTKAO50mH6sEIzsfglJBsbRjKqE/jADPAUe9H5VhNRzrDBYWK5qjPXU8u6OmcOj45YCjoyNCADaT8AAvsjvnx9fvz//3wAushXgC/t8IeD3sDQZCwfC4TCoQjkUCQaiMfCkSicYj0bjMWjYYTMdi4eTScCQJoIPseN5fM9QLt9uR4E8QAAlADMAGFuQIebyACyC4X8kBUnJ5XAgACMcpAPzYewOzy5fIAbIK+QB2MW8/VS3L5Fhy0XKllq9lczWGwW63na1Ccx1G1DS014OX6y2qtkczl2gUux0hrmO0XGmUsABMAAYlZ8Vaz1ZyABy8gCsgsQ2clHpNsrlsaTzP9aazvMVLqrpejXvlpb9qZtQvTOt5iANHZd4u7gJb1sDdprtt5pdD1YdE8lQ4D497Ed5S4zTsFmf1LszHYbstjivnadjs5dJ/DnJPopdfMnXPF9cLMbwsebVIAjtRKHIhCZcK8U2HDUJRvEUDQvcUoyffIA25ZBhWQLNB2TK0FyFZ1gK3e91z7e09wDBCs2QTVkPLVsRxw8csNdSiaPdEBPTIZ4iM1ZBdVIwC0ODQU7WvZcL0jAsGKLANWN1ZB0w41C0zlMCXVknMzzkrkTxzfDnjlZBY0Q5BECkis2yrVcq27F07VXO0B2gpj2U07SiL0t5ByAA=", "641958273539217846872463519915324687486179325327685491198546732753892164264731958"),

            // "Fortress Sudoku" by glum_hippo: Maximum
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QDEB7AJzWJjDAAIBlAVwBNCBrOkVAQzrQAsSEQABR4QsEAA7iqAISx0AtjGRUOzDlQDmc+QH0RkwuxDE6OMDDQCAMjABmaQgDcYxKreKF5VACI0AslQATAAMgQCMAHQAOgB2sQDi5ACeVADGMFhY1KmEMWgcEDFUDBAamNRYHMQaLlS8HEUNKSS8hBq5HJkpHAwAVhzpeVQA7iJoMGkZWBFGGsQQDAgA2kugjp10uGGoZc4xCGSbAL7IwCdnp+dXlzdnALrIq9cXL88g63K4AOw7EHsHJhgzyODyep3eG1wAGZfv94IcgbdgUjwR9NggUCBdjB9vDASDHq9bhDPggACywnEA44oon3QnIukk9HwACslNxCMZBNWzNwFKxfypeJpdO5qMhCAAbBzqYj6WCxbTgaCmWjcDLBXCucT1Qh2VrhTqlQqTeK1pL4AAOWUi+Ugh4geQcAAeEHkCmWoHSmQEACUpQBhQIgc4gH1Yf2s4Oh8ERqOBqGx71Tf1kxPJ8OpvB+9NkzPxnPR1kF7MgAOBqWl305oNfauRouB+thwvl6NWhtpwOd1tlv2BZtdnODqt9mvlwcl8eNyeB/Mz/2DpOLnNWjOr8vrhdx/vr6e7id+9djw+z49DkFHIA", "194573628528694713637281549769428351382165974451937862843716295216859437975342186"),

            // "Cloneways Game of Life" by ahaupt, Botaku, Ben, Xoned, Philip Newman, Hackiisan, ICHUTES, Qinlux, Gliperal (Killer, Minimum, Maximum, XV, Arrows, Clones)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QGEsB7AOxgHcBDATzAAIBxSgWxjqIDM6AZCDuVJQCuaABZEATghAA1ShIhEhYEKglCcYGGmkA5Sc0pY6AY2Jk66zXUoAHW1mp0AFCcoBzGPU50xbMCxsJkTEEjYSbBAAJjAkGG5YAJQAOiT6EobGzBAkAPSGAB429o6p6Zl0AIISEkTklhpexQ7UZQZGdAAauTIN1nYtLiREdGTulBgAbkGkYGgSlDloKSQAQtTwqZSiwrZoqatEaJQA1kIHsamdpDBRqQAKohBYELZ0uhSGJKkAEpQmJwgEAC3xIAEl8D8AKoAFQAogBlVIARRyWCEBVSDBethgCywqhA7gUUQQAG0yaATNIAMSrAAMADFGQyQABfZBU2kM5msjlcvA05m8+nszkgamC4Us0X8uWc+XANkAXWQlIl3KZMrFApAdK1fPFkr10sNuqFwrNGqlltlCqNmpFOutep52rlqspiv5LotTu9DsFbqtxv1/oVnvNzMq9JjztD0djdqjjJjcYDSvtmaVkYzvtNyfztp1PoTqaT8dpifTgZN5fTuazparxebNvDKY73sb2cVRa7tb97sHBZLPd9wcLocnJabI9b847/fd4+nBqnjuHvbnna3KtVEvMuHVJhgWCwKngZJAACUAIz4ADMhPv+AALC+AExPr/v38AVhfR8/wPMwbnwM8L3JW9/z/VAb1gwD4IANjg29UKQ9D8GQl8AHZ8EA5VS0gy9rwQtDyMwm9UI/FCCJfVCcPg/DCNQMCyAg89SNvfDcLw/AAA4XwE/A+PgkShPE/BEBfRBBJAIijRI6Cb14/jJNvESxM0+SpJk+C5KE0Cj04qCr1vB9n3gh9aNvb8rLsijvyo4CP0UqllPM187xfB9P1/Hz4O/fzjPAzyyO/bSb2/DSb2AqLgKM4iuJUyLf1i+KgPk0KOPCnT/Kkhybzkgrbzk593IlPKbxE0qap/Az8Dq8qFLYkzqofQKLKagLfxC5KzLI2Cotg2LYJknKYFM7ibzffAutmnr4LmiqBpmuaFrmuqVtaw8wpSrzhpfUbjukhSVUEGo6nJUAXjIUiyJ2+DYNs6j6OY7DhNE2TstA6qtpfHbKrurxySGs7nt0mDvuVP6DvBiafRBh6sKY6G0cWjG5r42G2OqxjzvFZGwcc2Lv307qJrhwbSfOg9shICBmCEZgbqq89pHq/yLpAQomZZtnTw5vBor/AbOfwnyeYKSZBeqiSvpx1BJiMIRcBATp43l77GqVkAVfRdWZHZUCPGPDz4e6ha/N6oKeoPJRsByKbaXpN33cJDhSDQfBXfdt2tct18ips38iu/V7nKytzUEdkHfcFf2A9QL24gTvUk6naqXtOqiaIY97Uf41iQDj5305pTPPe9iuq7WlS1I+2KtK+5uIbK7LY5EeO/f96u097j368OnXobGs6He78vB+TkBU59mes6DjbAaW28ga7p2OMX/uF8TpPA5p0WopirKEs70up+3/e+5Tmud+Hsjaq+oqSp+irN57m+Pbvgfv4DlUbIgA==", "256734981731986254498215736819452367542673819367198542984521673625347198173869425"),

            // "Quadiagonal" by Tyrgannus (Disjoint, Quad)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QEUBXAQwBMJiBzAewDtisRVjC0ALagJwRABUBPTpWK1ahMExCdCOMDDQ8AtADkuAWwYACMIVLUA1oU3TZm4gAdzWfgB1aigMpoRpYp1KbyYAFbUItNE0AdzYYThhPCEpMME0AYxFaakCAIwj/TXYIsGI1CPCwcxg4zAA3CLiYLCxNDLUZDCt8mGi6MDtFAHU2TAqITjjTNwiARxJSWJCwiJhiOLZNMTU0zlraTND4/sGI+rBUiPMCmADtajyp8LWNiqqsWKz4rEJ0h+pCef9KADpJSk4IKQEABtYHAAC+yAhUMh0LhsIRMIAusgwYj4TDMRiISi0Vj0QT8bjsYSScjUWTKQTiaTaUSKXSqeS8UyqTT8Ry2SiQF5fP40P93uYJPA0NIYKgxmRpE0QaBKtURcCQAAlACMAGEABySdUaxC6gBM2qN+pA3NKDBeSrVyAALMgAKzIRBI2EgBX3EGqu0ax2630ANl1jr9IY1wYtVpgNvtyC1LrdUI9dyVqoA7BrDbrMwBmXVarMFjX5qPPGMg3PIdPxxPuz1plWZnWoJtm1uFluqwsGsvWkGG5BVwPVpPy1PevXZ1ua/Ot43T1XG0uoS3l2OD52BscpxWT3Ma9O6g9dlW+o+t306vsV+DAu07huTwMl3Uvu051+tzMfm9Kw2PhOd6qgewatgeF4+hGAaHuaq7Rkqw6AXuwEqsaaqmouKoHhh4FFn+A7IV6qHGmBS6wXhZHYbBBF3oOVY1lqRGNqGH6tqG/qtu+b5hrRwKuvWQHKiqha4d2RatogGpiSqUnZnxVaOm6bpAA=", "761839254325467819849152367972318645153694782684275931497586123238941576516723498"),

            // "Just add Color" by Polycarp (Disjoint, Knight, Kropki, Thermo, Little Killer)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QCkBXMNAAgEEATS0gYQHst7CAnEVAQ0LQAt628ABUYBPAMYcWAB3YgWhHGBhoEIAHL8AthyykAyoUr0A1oVIAlBTDAA6UgBEIYAFb0IAOzJK0YUmPruJCwcHmh25J4QALTG7hAA5jxk/oFowaF2AEJYHGLGpEY+pJIwpACM8ABMpMEY9HYaZDq6hb7xEABuMO52emgc7pSSNAAqPDAsmvQ1VrakGpM6fhzxpfKKDVpLADKY2KUA0hBYOCz6hJq+6zA2svEsEJQIANrPwAC+yB9fn99/vwCfgBdZBvQH/H6QiEfEFgqHghHw2HQxEo4GgtGYhHI1G4pEYvFY9FwolYnHwilkkEgAYYWIJJIINKEGCoShOVyhe7MKRgJnyVnocaTeiaZQTF6gLAeawvZ4gcwAFloiFk5gArCqQECgb8QNL3LL4K8FZr1WqAGy0c06vUGo0m8wADloFrViFd2t11OlaH2xmOpzAF0lIDEMBOqnMZQADLRFbJwyc+caFR7zahnZ7MwB2Wg5y20J1qzWq6nslgwMR1dyqACq5lkHR0LNUAGYYyB3tTahB6KGk1gU/Lo7RKmqyrQ29q9YPhwrJwnM5Pp7qvmGI0OXgvrRP4zP13Pt+Y21O1ZUz2vQEfUyf95nT6vZ5v53fx5mL+Orxvk8eL6qHy1b8bxHPNi0zF1i2Al9j1PJcFWVBNoN/W9lQzBD92Qrdb01MpCzwrDX2VadM01J9Dxg28LzdD9d0I49JwLWiC3o29TwAhVTyg58UJHTUmIVK0WJ47CRytGjBPzA9r0o0CxzVF0vxE18PTwzMPSUijeIVPN0KzG1lIY7Md2ErTRJ0rVcyLGddSAA==", "912483675786512394543697812294368157678251439351974286839746521167825943425139768"),

            // "Hay Fever" by G (Thermo, Between Line, Kropki)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QAkBDATwAIAxGANxgCcRVCBXNACwHt68BxBkWpjjAw0CEAEEADpKxkAdpwC2hLKTBMAJuwDWTUgKEA6UuKzs5Ac1Js6i9opF0wyUhogXMYUhDkBjWjCEwqQAZrT21qwwpABGgjHGACLunqTmpFg+MF6+5nIwvhiWkbb2jrReikxgaLHRMSIA7jAwcpHswXFYMaRyTIoNFcYA6qyY0VpoOXkFtblywr4sEDSuKVOkhHIasViEvtqu7Bvz+YVrHhuNmKybpACM8ABM+oQY7MYAcsebWKqTXkIAVIHhockMfAstAgGgQAG04cAAL7IZGolFozEY7HogC6yEROKx6JJxOR+MJpKJ1KpFLJNPpeIJjJZ1LpDI5tOZnNZTMpvNZ7KpwsF+PQUVodgcaDo8NAmXyYHhcJAACUABwAYXVfFViE1iBAuNxGJACuyyrVAHZNU9dVr7rr9XbjabzUr4Ai1U9NVbdfdtbqfYbXaizVkPV6NYHUNG/bGbQA2XU2gCsKc1ABYjSaw+7LdGdQmY2rE77dany7HM+XQ/KIwWfXbYwGAMxBrO61ua9N18OKxu2ruax3Voexyvtvv5z0q1VN/1D11itwhEJ0Vq+XCIkBbv6R0s9jO9017rAH1WV5MT2unmD7+GH6/WzXJ3OgM8XsvP1Vlv0msVaDeCB2DlXd73PR9VW7Zs1W7Kc7wfT1vU1dtY3go0kTFBo0GaVpzTAmcowXdDUN1GtsxvdNY2/DN4zVLUdWnBtZxfWDfzIm9KLVGtqLg18O3/ACkSAA===", "197256384352841769684793125275369841418527693936184572821975436549638217763412958"),

            // "Dotween" by Jodawo (Between Line, Kropki)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QBEB7NAdxhgDsRUBDAVzQAtCAnBEAKUIBMaTDqIFnRxgYadgDlWAWxpYABAGU63QgGs6C4aIU0ADvqwBPAHQAdCpcl0ZAIxgswerIQoBzPQvMgAQuLJKBQAZCAoYHwUZOjA0BUYaADcYL0T5OhSHUnIKBSYUtKwM50IAMzzGFIpbBycFNwVMZwBjCBZm0WQFCmJGig6YiGSLKwpfLBpm9QUAaRZCfXUIBSI0ZzDuCGaaNBhnGgUARngAJm0diEIFLMDc/IVN9ybTBWk4+UVxyem5haWV4j7FgpJ7JCgjSwAdUYmBSv0Wy1W6wom22u2c91IV2aMCwWCBKWabjEzQYQxgLzeLkU0Nhs3mCIBaz0wIUoMopkEMPcjCwEB5aAAwm5SnzmmsEKV5GJUO4WBBuAgANpK4AAX2Q6s1Gq1up1+u1AF1kKqDXrtRbzerjabLWb7XabVaHc6jSbXR77U6XT7He7fZ63bbA57vXbw6HjSBNqVSo5KDjlaAcXiwMqQAAlABsgoAzIJs4KACwgQ06kAp/Hpwsl1AZgDsxdL5crafgSszjdrnbzzc1FdxVfbmZz+brjfzZf7rerJ0F9YLc4AHH3k4O2x2M4d54ud1O16nq9uTruT2WoywLgJ2weh5ui4KAKwFx9P1cDw/DjOv591nPP/cPzvTMHzHECm0AmcvxzLMCxzBdIPXasH1gusHwQlskK/XNBVQ8DYMQz9NxwvCMxwjDpyw4idzQvdMKIzMl17OsmLPejgIzJiwIzRBe0IjjeJXFjBRXc9UBuHI+XCJMQCkvZlWo39wKU783z/NSewAwC5I3TdX2419u1UlTX1I18EO0sJ5PbajuIfIyfzg3CC0bCzyx0hSRybcdmMYwUTzrXjDlLSzwl0/CC3I3chMzbdEBC9yrPCjM53iutl13Bd0ucw1QuslU/OC4SAr87imJLXLErCzzCxihsRILJisr8gi8uSucjLnbicJKjMHzPc81SAA=", "134578629625439781879612453496381275213745896758926314567893142942167538381254967"),

            // "Sandwich Sudoku" by Cracking the Cryptic https://www.youtube.com/watch?v=2DN32fY63JM (Sandwich)
            (@"N4IgzglgXgpiBcBOANCA5gJwgEwQbT2AF9ljSSzKLryBdZQmq8l54+x1p7rjtn/nQaDQANwCGAGwCuceAEZUaCKJgA7BABcMsgdz56uR9sMMjqB42YumrdqrXrhxa7AHcIAYwAWYaQFt8UE8YSUkEEAAlAAYAYXkQVAkZORAADhAKEBCwiJjYgCZEkGTZCIKAZkzSbNDw+Ci4qqSpMob5ADZq4Lq8uIAWYtLU+QBWbtrchvzxlpTyqqyc+sbYrrm2kHlFmuW+2IB2IdbUiuiJvem4jI3UgqOl3qvYxGP5hp2eqaj5WPPbiKdC5PKIFP5vTYPXYgyIVcEAj7A76RfrwkonQGDR7I0Zo4aAhLYlaRDp4jENAqE6HIg5k95bV5EvJpOmbCqfSbExCsu6ZWhEIA=", @"859274316317956248462813957523749861974168523681325479295681734148537692736492185"),

            // "MiniMax Sandwich" by Lisztes (Disjoint, Sandwich, Minimum/Maximum)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QFkIA7CfAQwA8ACAZTKIBMB3CAYwAsRUyBXNdgPYAnBCAAyEMFDQwwXEEJ44wMNKIBywgLZkstHgwEBrHlUXKqZAA5WsATwB0VAKJkOZgU2RVWArDy0ib3oGKgBmCjCqACMBal8iNDJiMCoYN3YqIgDomCEqADMhAS0qAEYqNAEqRDSKN2w7KgEiVhgHAB0iLoARSQArAWI0fUMTMyVZSxt7JwBhGCwsVP4yEesrdPziSvYYKjAyLX2rAUgMFrNFtYgAN32q3ZgIfIYYAt4sEaEYAHMIFqpLQ8MAjIgCEYJJI7fj7Q7HLI5PKdbpEOiMFjuGgGYymcxTDazKgAFT2iK0uSEqQEfEgbyeVF+QggoTAgiYDLAAWaBQZDAg/zQqUOGLYe1CuTQTBgMCI5UsjBqVBh7DWHg5wh8fgCRBRXQA4j8mm0lqkWPxLEJikxUh9WMRfs0+EwyEJQsDQVrEsk5dkKXlUkz0jJ8qs5bo9MJ+AJfi0I00yAx+m5ZZDFss9URDTBjemzZhMq7rba3A7lUQXW6qB7IS1ob6kVSqMoVqrw0tmkJo7GiPHLEmU4kfHmUfImSyEABtSfAAC+yDnC/ni5Xy7XS4AusgZ+vV0v93u51udwfd2fT8fD+er5vtzf72fL9fnxe7y+H7eTx+H0/T3+f1uID8mAgzDEyNJWHI8BoIoMCoCKzBilyWhTqAJpYKIABKAAMcwABzyLcug8LgIBlARy4gOhWG4QA7IRxGkQATPRlHUXgmEAKxzNhDH+MxvFsemWFMTxfEkaITEAGwgEJSw0XMnHiaRFGAVoxAQMCKHwDOVHCRxolMbJC56fJBn4cZaH6SAmF4XMRlyRhHF2RRJnsTZ3FKbOamUJpASoaZTkefZlmBVhdleW51lcRZjkiYpoXuTFiCyRus5AA==", @"374526198956781324182349765719465832463218579528937416247653981835192647691874253"),

            // "Return of the Revenge of the Hash: The Movie: The Game" by Lavaloid (Irregular, Thermo)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QCUY0BXAJwDsACAewDNK0ALGSwgNxnIHMW6HnKACQCGYRvEoAVAQFlqbCDAnSWAcWEBbOKmHEm1UghAAZYW2FZqEACYhUpYjjBEjAakoBJUqRhdHw0kowYmtqAGtiSgcnSmEABzisAE8AOgAdcgBRDlIkqOoAd2RKAGNqLGINcmLhcmtKRABaEpgsLCjfCGoqDWIwNFLutGEIKgt26wguTDBKWlJqDUoARgZqBvTyDPcVUg116JZ4xMUwTYARKZnKXv6gtFIIEuw80ZKfURZ5xcoAI0dfmtKJxrCk7CAuI9bPAANow4AAX2QiORSNAPmm3QQABY0SAMV1yDi8QSsfAAExoqmogC6yHh1JR6M6ZIADCSWUT4LjkfjOcTUYLEXSGUK+ZiuQBWDkShDS3mkqUywkC5my7mMhEitUq+DynVk/Xi3VGxUIADMyrJloV/PgNoNXJ5joQAA4rVz3drjYaPQgAGx+vVBh0+rnm1DmCq4CMQiAcLkPYgwENB51hhAAdiDXvpLuDtvVgcLuuL+dDZvtqZLZPTlezNc9WrzGfgyyD7bFlbLrZ73aDDfzg+b8Nbnfz47HQb7drLA813srk6XHdXQvn65pdPQzD2iyIMEMsNAWFGMDACDhBHNAGFluD8LfyQ/sTfzQ/JW+QFu8afyOfLxha8b0lB9b2xB9yS/VB8GWL8f15P8ANhIDHxvf1IJvTMHzg7CEJPM8LxQgh/Swj8yJg19sJg29XTAm9EG/Glf0IwCCEQCiCFdTj8EzdCmJY/8iKvXiGIfPi6JgvjqK4/j8JAJDhNQ0j3ykm9nzU+95MUtjRNAtSIJg7jVPY+DmMQ1jiPwDj9NMjCf2YoA=", @"825461739314987625673215948768192354589634217492758163931546872246379581157823496"),

            // "Peak Sudoku" by Rangsk (4x4, Little Killer, V)
            (@"N4IgzglgXgpiBcAWANCALhNAbO8QAUYBDAawAIBlAVwBMB7EqkVIqtACzoCcEQAlIgDsA5mBLMQXKjjAw0vACrsIYMirIcYamsSyVaDKgDoyATTpUyAWyIBPMoLpoyWCCS2ZkZAEZsNy1XUAd3YiZwAHYnJIrgAzbhtBAGMtLDoGVVd3IwAdQTyKNCEaIi4aMgA1eDIAEQhhTFVZcNKwmHLveyJKsjAqKw06MgBWEwA5JzIiLD0K1VKtBoA3GEFc/MFC4tLygBlMbC0AaQgZmC5quoa0ebSRKbJwughBNHayGggiYTpBad7+oN/FoklgqFpBDAAB7ONBDTRTLhcOhBExXRpkJJCMhcGCRMJTO7CB6fb6/aZGCTCLgQGgIADa9OAAF9kCy2ayWQBdZBMzn8jk8vkckXc3nsiX8rk8kCuNCHEinHBcPpWBmgFIzXh8AAMAGFhhJNVgwAz+ABGPWICR8ABMeoAzDaHXrbTbEHrzSAZZ9cUkML9eDVdhIltNwbxzdbOSBjdr9TqjTAZqb4PSLZ6bfa3ag+C6nbmPdafRA/QHBEG+KHw7gQAA2EAxuN4PiWxOoY2p9N2zO5l05/gep0lssQQN4GpV1BhsG1gCcjZlUKW6tjyZNZtbrptlq9MpnEbwFUXzKAA=", "1423324123144132"),

            // "Behemoth" by Tyrgannus (16x16, Irregular Regions, Killer with no sum)
            (@"N4IgzglgXgpiBcBGAbAGhAFwhgNneIAQjABYwC2A9hiSOgIYCuNlATgiACoCerA5vQB2gxmDohWjPGBgYOAWgBybcvRwACMIwAmlANaN1k6evoAHMzm4AdQfJQAPFOoitWMPlPqsjHiJUEwW3kAEQg+bDB1AGMhQWpfMxh6DBdBUxj6PhhbcT5WCG0EAG1i4ABfVAqqyurQdwiAhAAGWok/JvhWqvbGwRa2hv9+rvQANzVGfABmdAixmBGMSRhBjpHENb6ERHHJ/AAOOYgFpZWt4Z2Lzt2QCZwphAAWY9OEZanrja+rnqGbn5IcoAXVQZVqEJqf3WCAATIDmnsHvgUK9Fu9ztDtl1AfCsZd4NNAbM7vs4Wizp98QDqSMSfdHkgSfN0fAPqtab96jCgZykEjGcyTqz2YDNqDwXy8dzsdLegTYQKUS8QCzKRyZQqEUqdkK3mzMZrOnL/iNFaTkQg0KrheriTr4ABWCkYqlGunEz09Bko81q10a+U091coMjADsILBIfgT1xuId4ZdBrdYbh8alGZj5p9Oz9toDXpjRL5JeLbVz8AAnMnRd6ybHa4a07yY5GJaBK3qRYbIZ2G4hETb9XXs32LYya8Oe27Ac6J8qm6nTQhHXPE0uNeOV4SHdb/SnAzuy1HJf3LfAjtO7VC6hWB/OD6O+/eL1On83K7cP7Pb6/GYgKo/luHYvnya71he+Yjr2kEAfuBaHmBdQLjs36Ic+f63i2iC3JWQEYecHYtgcyGofACEwb+MbIICtF8talZJtehZYXebFiuK0aAnG2Fft2N4xqRfLCUJYrdDGU5fkOwGAlWckKXBMybtuPKRjUoEcSJgJXpWlEzkePKDuJimSQ6uEqXy8lWQ6j6EdRgLqRUmnngBlkxsZ2E7oODoCaxHlDkx7k4YFA4yfZhnYp5kmmS2UkNgRVGRQS1nsa5+B2UlELEd5EkhQ674ReJDpXrJfLRSFxXkYlBkmeVZY4bxZnkcxZUxTZ5GAe5YrztJ3UadGlV8QONWCeRflIVp6UIIVWVKQgpVFWxcXjl+mW1V5RlcWeq0PsF3nSrtUHBV+6FzR5DVftBG0ofxJ0Not53Zdx81IOFT3DRe+mCf+vr9RdYqXQOE2YWlz07ctlbrWN3lNY15mjf5jW/QgrVLWDU0gqCIB6BAOB4KwsTZCUoDRDA+NiPAxQgAASrCADCsLiHT9PTMzDNPOz9OOsz0yM7zrMC5z6A03zPMi08/MS4L0vC7Tkvi7TjpS0rMuq3LNPKzzwJtGTFMlLTDPhlzBxc1WXODgLxsi3zps2/T5v25b0vW/L9N227jtu87quu5r7vM8rXv+5bOs9HrOCU9TLOIEzIsM4gbPx/TgEW4rosp3HtN84nAup076eS7HzNF0n3sa0X6fK8XIvV2XIca9X2u6+TkcGzT4Yqx3avdxrnfpwcXeD/Xg8a4P6dVl3k/15PGuT+ng5d4v9eLxri/N+HrdR7Tnd+53Hvd8Hnc+zTg9+4PB+D8Hg8n5PfuTwfk/B5PJ+L37i8H4vweL6HLf61TO9M7M2PvXY+fcU4DyASLG+I8U5jwgczF+WcaYvxnnAxBCCRY/2QT/FezR0FYPwYgDepMt7t1jlA2mFDc5YITmvBOC8c44JzivHOa8c4LyLjg0uzNAIEKoZXXh1ccF1yEfwmmxCEFh1If/aOFD34M0/gzb+Cdmi8L5u/W26iHbaNfpLd+ktP6S2/kXNRWDlbv2Vp/IOYjf6b1kVQhmOCGYrw5rwhmjCl581YfTdh3NeGS24T3Pha8FZiJEcE5Wa8tYgB1jrIAA", "91510214513128641671311116128416710152913141354316149131165110712215811375152812163141194106311210112913144716568151384711156529112103141612141512316865111071394516961014473131581112212511138915110121663144710734126214911851516113141616371011131542895121598121345161731461011216214371113986514151210645151613211101291387147101116812154141332516981213951014471621511163"),

            // "Irregular 7x7 Kropki" by tsc (7x7, Irregular Regions, Kropki no negative constraint)
            (@"N4IgzglgXgpiBcB2ANCALhNAbO8QEkAnQmAcwFcsBDQgAkQA9FaBpQgewAcBrCEVKuTQALdoQTowAY34hClGGBhoJbLr1rycYWlU6csAT3i0AOgDsAtLQDCMLFh1LONKmhgATWgCNDu2gDuwpgwtB7saLRS7OZoVBDmUTFKUkIQAG6hHhCkmGAAdLTWdg5OMC6Ebp4+flQ+1FLcYRFJsfGJ2bloOgGYwv4AjPAATJpuEOyFFtYA6n3sQrTmZOOZrWBolQloyLRg7EstVA60MAwQGwmkzd26JLS5meb5FhaypIQQHggA2j/AAF9kIDgUCQeCwQCALrIf6Q0EIiGgmFwxHwpGAlEYsFyMgTcwIAAsOJIuRiCAArCS8eT4FTgbiyQS6dDYaBSfiEABmalM7no9GMzkshkc2lUrFC2k80U05kANl5wuJsr58BV7LlRKVtMVyLZUuZMuxaNVwolMJA2QAZtaYCRzFJcP8QE7Sr8QAAlYY2Qmyb02CkgKE4t2OD0B+X+n1BkMMsNgCMDGxR1Ce5OIYOh+zh+A/L0Umxc/3yotZ+M5xN5r1cmwDf2Euvl0AJiOIMtp9vDZuuysRwt+tOlv1xlt96uewtBoeBrOWyoYdi/MfuifJ4tp5Mj7Or/PplP+5Oxne5vc+jden3biu7r3J7tpn3d0e92+e4cl2cv1sTxsXz2Ns+J5VnuhaZmmjaZiGIZAA", "7412365123654723574163125674476315256742316541723"),

            // "6x6 Extreme Killer" by Leyrann (6x6, Killer, Minimum, Maximum)
            (@"N4IgzglgXgpiBcA2ANCALhNAbO8SIA9EACAUQLQCcYBbGYgaQix0pFQEMBXNACwHs2eADIwAnpQ4A7KexCUuOMDDQIQAWgAiMAGbcsaYmC4ATfgGsuxBUuIcADvaxj4xAJJTiMAG4xKY634Ad2RiAGN+LC4aT2kTYgAjfgJQjhZiEwgAc0wwYh1KfhpiAEZiNH5iEl4OX3LKh3sYDkovAg4w7AD+KTCYADoAHSl1JhY/I1MLKxsYPMbnV01s3OIIWPCOLPoauorJ4q57evLeeilohIn10/oK45wdQwjKKQn+HVvN7eIACiCzp4cr4pABKfrEZY5NB5GgcAJSfiGahNDiGG4cb4DYbqACy6wgNGiAHo4QRCdFrIo5nZHItiABBDIrdEbMAARy4LXoQUwvDslEKQTy9n46wwUiyxH4PCCLXibxgJjy+yuxCwLW2rT40js6T49Ey0PmhS4UnimFSzOhazZnO5xF5fC+LSFIrFUglUvWcsoCpgSpVlTVYDh421NVi+rO1tWLRl5rWaH6cl42V4WHTaAAwj0dJnOmAEHosMpUFlKBATAgANo14AAX2QjebTZb7YbAF1kPW233WwPG93e4P+x3hx3R6OJ2PZ62Z1PJ0PuyBzMxWGEtrh6yA+iwi/AayAAEolbMlOTHgBM58vAGZb6hjw+ryAV940lxcCASgAOEBtruMD7rWJ4ACyPuB2avu+n7fv+gF7qWoGntmYGXmeACsb6oB+UTfthiHAchh4njer5Pjed73tm1FPhB1GdkRIGkcemFoZeiAcbB+FqIRzZASxR5sdBl7sXRJ5cYxuFwWoJQXl2qA0ASRI0LWoBIWoImESuZIUmph4acRWk3ogAFMUAA", "615234423516351642264351136425542163"),

            // "Eddies" by Qodec (Arrow [including 1-cell Arrows), Even/Odd)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QFEATQiGMEVAQwFc0ALAewCcEQBFBwmAYwpCeo4wMNKwByzALaUsAAjDVCDANbVZAobMoAHbVgCeAOgA6AOzMBBJkwYB3MPFkARCAHNMYLVganXW01rWdrIQAfQwsq5MEISyktRgaPLUkrJoDGl0ESTuSaEhaJ7cEEzcOIbObh5xlPrqMNowlEkyPn6UAZRBtibmpgDyxAD0MABuMKaOAMIwWFietph0/rIM2pQAjtQRUTB1xaU4cQlJAEYRnIQVM3MLSytrm9uRTHvyW10R8Ymy57JjE16fCiMQQAG0wcAAL7IaGwmFwxEI5HwgC6yEhKKR8Jx2Oh6MxuKxxKJBLxJPJaIxlJpxLJFIZpOpjNpVMJrNp9KJ3M56JAl3BoG4sywrAASgAWKYSkAIkDCubiqUANllsPlIvFyqmqrlCtFeDF2plUL5ANMgo1isNAA4pja1ULNYaAEz2x1Wg0gMVul0e/Xiu1+01UbqWrChMjgsHegDMUwArHwfYnkwBGVOovn68jwGNi+NJ1FyiOmKN5/MJqax5NVv2oMVVtMgLOoHPg71VmvF9Wl8sQztTADstfdDariBb2ZFucrw5bJcjs/zQ9TDbtSYbiEz09uHbFq6Li7Ly+9q8n66mF+92+vYu3DtbntnZ6vC97S+jr+bl5/N6m9bena3a7vM+6rs2PagH2p4FlMf4pghGZ+k+7Z5nG8HvtBn4VhhDoNm6I4Nhmj6gS+cGPse/b5vGNYNvGMoNlKm6DkWGIYXR3puox3oZjKqEzvutFYSAMFfkaAHJquIFtoJ6ESShppQkAA==", "526419837148375269397286154654921378279834615831567492783192546462753981915648723"),

            // "Orbit" by Qodec (Arrow, Little Killer, Even/Odd)
            (@"N4IgzglgXgpiBcBOANCALhNAbO8QHkAnAI0xFQEMBXNACwHtCEQBFegExgGNyRCqcYGGmYARCAHNMYAAQUs9AHYS5iuYUL0A7jIhq6MGRMIR2MgLZUwaGWCrmZaeo9qH2kzLrXSZXCIS4cADoZcSk0WXMKAE8ZQhgABxgKG3klFQo1Cg1tIIAdRQKwnzTlGXcKCSV5WT13LhSYM2JY7M0tWXoaSE4XQ2NTCysbOwcnPqMIADcYNUV7YhhCORs6FPV2kOKIixi4xOTUhTKKcohK6qx8wsUAYRgsLFktTFo5I3jYvwCcIesZRYyDjsEL3R7PV7vYwwWJgACOVGyhks/0BMBmiiCvAG7AQAG08aAEtlMNFmABRDEgAC+yGAtPpdIZzKZrKJJLQZLw+HYuOpAF1kISWYzRSLxYLhWzxdLpZKxbKFUqBULlYqZfT5RrtXLVTq1WKterjYbVSBiSZOcweXyTfrzRyuSBKbMafzBSAsJhsDAANYQR5LUb40BcB5YZgAJQAjAAGW4Adl4YfB+JAkcQtwAHLxI1nbogQB73PEuBglMwAKqR3hTeRUXAgHMMkApiN4SMAZluceT4bAacjACYC7no9mi6gS9xy4oqwAZWv1xtJlttqPxzt91PwPHp8dD3Mj6OTkDTssQCt4USL1B1rAN5gANhpdNb4ajCduse3T0H+ZPVAM1uQ9i38GdLzna8azvZdmELNcPw7eMk1QNsB13fdbhfICRwAVlzbsABZcyI24tyAvCQNzJ8e1Pc9ZzEW8QHvR88E7Zs33XDsv17ND+0HWjC0oicgLI1D027HD03wsdbhIsDS0YvBK2Y1jGyI1cuKQ/dN1/DC92AkigPzAigK/aTI1oiTIyonMxNHRSIKvEBqyXB8NMPRDHijbsf34ndDLIwD0yow8gNoij0y/Yz01M3NMxfJyLxc0QYJYuC8CIrcVRANptBDT09BgAzDO7GyR0s8czJk+Siw9dDB3KosWy9RQSvxMryNI6iRJCqzqPdAK/0wrtuv5VritK6LupMuqgMzGq82w+rhoMmatwmt82o63dDK/GzaPs0LRwcnMhvfQKZqTLbQB26bbMTHqkoagTRqom7Jvah7uyWsiFNeq6xoI26iu+zqTqigaAbWwcqM2r7doJGaltol7YdGr8QYFakgA", "654817329189263475237549681925471836473658192816392547368725914592184763741936258"),

            // "159" by zetamath (Renban, Kropki no negative constraint)
            (@"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=", "153428796298675431467931582386152974972843165514769823639517248745286319821394657"),

            // "Alien" by Lisztes (Whispers, Thermo)
            (@"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", "497258316512936478836714925381675294659423781724189563163592847945867132278341659"),
        };

        static void Test()
        {
            foreach (var curBoard in uniqueVariantFPuzzles)
            {
                Console.WriteLine($"{curBoard.Item1}");

                Solver solver = SolverFactory.CreateFromFPuzzles(curBoard.Item1);

                Console.Write("\tCountSolutions... ");
                Solver solver1 = solver.Clone();
                ulong numSolutions = solver1.CountSolutions(multiThread: false);
                Console.WriteLine($"{numSolutions}");

                Console.Write("\tFindSolution... ");
                Solver solver2 = solver.Clone();
                bool foundSolution = solver2.FindSolution(multiThread: false);
                Console.WriteLine($"{foundSolution}");

                Console.Write("\tFillRealCandidates... ");
                Solver solver3 = solver.Clone();
                bool realCandidates = solver3.FillRealCandidates(multiThread: true);
                Console.WriteLine($"{realCandidates}");
            }
            Solver.PrintTimers();
        }

        static async Task Main(string[] args)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Test();
            Console.WriteLine($"{sw.Elapsed}");
            return;

            // Useful for quickly testing a puzzle without changing commandline parameters
#if false
            args = new string[]
            {
                "-g=........1.....2.3...4.5.6.....2...7..7...4..28...9.5....5..69...1.3.....6.8.4....",
                //@"-f=N4IgzglgXgpiBcBOANCALhNAbO8QHEAnAe2LQAIAxYwgYzlQEMBXNACxoRAAU2IsIAB3IA5GAHcAtowB2IVIWY4wMNFxE1pWcmGYATYgGtm5RcvKNBgrAE8AdOQCCMveXaM0AcjDlMAQnkQAHNCCD0EAG0I4ABfZFj4kAA3RixmXABWVCCIJJg5eDRFGDiEstKKxJS03BRg3PyEIvSYgF1kaNLk1PSEAA5shoLmkvjK0Gre+AB2Qbzh4vGuydwANjnGwsWx9ujumoQAFg2FlrHziZ7cAGYTpu3yi4rdsv2p9fr5+7PH15WEABMdy2PzeuAAjMCRksXksnvDLgd4ANPptoTsOoippDUadRr8wUcoQ84X8rghbrjviVYVVyfAcTkviD8eNCcjiT82f94ECqSznpjSdz6YyhtTlvTZvz0eVaWSkXUmWiHuysjKSQjnq0YkA",
                //"-b=9",
                //"-c=renban:r1-6c1",
                //"-c=chess:v1,2,3,4,5,6,7;1,1;2,2;3,3;4,4;5,5;6,6;7,7;8,8",
                //"-c=ratio:neg2",
                //"-c=difference:neg1",
                //"-c=taxi:4",
                //"-o=candidates.txt",
                //"-uv",
                "-s",
                //"-pl",
            };
#endif

            Stopwatch watch = Stopwatch.StartNew();
            string processName = Process.GetCurrentProcess().ProcessName;

            bool showHelp = args.Length == 0;
            string fpuzzlesURL = null;
            string givens = null;
            string blankGridSizeString = null;
            string outputPath = null;
            List<string> constraints = new();
            bool multiThread = false;
            bool solveBruteForce = false;
            bool solveRandomBruteForce = false;
            bool solveLogically = false;
            bool solutionCount = false;
            bool sortSolutionCount = false;
            bool check = false;
            bool trueCandidates = false;
            bool fpuzzlesOut = false;
            bool visitURL = false;
            bool print = false;
            string candidates = null;
            bool listen = false;
            string portStr = null;
            ulong maxSolutionCount = 0;

            var options = new OptionSet {
                // Non-solve options
                { "h|help", "Show this message and exit.", h => showHelp = h != null },

                // Input board options
                { "b|blank=", "Use a blank grid of a square size.", b => blankGridSizeString = b },
                { "g|givens=", "Provide a digit string to represent the givens for the puzzle.", g => givens = g },
                { "a|candidates=", "Provide a candidate string of height^3 numbers.", a => candidates = a },
                { "f|fpuzzles=", "Import a full f-puzzles URL (Everything after '?load=').", f => fpuzzlesURL = f },
                { "c|constraint=", "Provide a constraint to use.", c => constraints.Add(c) },

                // Pre-solve options
                { "p|print", "Print the input board.", p => print = p != null },

                // Solve options
                { "s|solve", "Provide a single brute force solution.", s => solveBruteForce = s != null },
                { "d|random", "Provide a single random brute force solution.", d => solveRandomBruteForce = d != null },
                { "l|logical", "Attempt to solve the puzzle logically.", l => solveLogically = l != null },
                { "r|truecandidates", "Find the true candidates for the puzzle (union of all solutions).", r => trueCandidates = r != null },
                { "k|check", "Check if there are 0, 1, or 2+ solutions.", k => check = k != null },
                { "n|solutioncount", "Provide an exact solution count.", n => solutionCount = n != null },
                { "x|maxcount=", "Specify an maximum solution count.", x => maxSolutionCount = x != null ? ulong.Parse(x) : 0 },
                { "t|multithread", "Use multithreading.", t => multiThread = t != null },

                // Post-solve options
                { "o|out=", "Output solution(s) to file.", o => outputPath = o },
                { "z|sort", "Sort the solution count (requires reading all solutions into memory).", sort => sortSolutionCount = sort != null },
                { "u|url", "Write solution as f-puzzles URL.", u => fpuzzlesOut = u != null },
                { "v|visit", "Automatically visit the output URL with default browser (combine with -u).", v => visitURL = v != null },

                // Websocket options
                { "listen", "Listen for websocket connections", l => listen = l != null },
                { "port=", "Change the listen port for websocket connections (default 4545)", p => portStr = p },
            };

            List<string> extra;
            try
            {
                // parse the command line
                extra = options.Parse(args);
            }
            catch (Exception e)
            {
                // output some error message
                Console.WriteLine($"{processName}: {e.Message}");
                Console.WriteLine($"Try '{processName} --help' for more information.");
                return;
            }

            if (showHelp)
            {
                Console.WriteLine($"SudokuSolver version {SudokuSolverVersion.version} created by David Clamage (\"Rangsk\").");
                Console.WriteLine("https://github.com/dclamage/SudokuSolver");
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                Console.WriteLine();
                Console.WriteLine("Constraints:");
                List<string> constraintNames = ConstraintManager.ConstraintAttributes.Select(attr => $"{attr.ConsoleName} ({attr.DisplayName})").ToList();
                constraintNames.Sort();
                foreach (var constraintName in constraintNames)
                {
                    Console.WriteLine($"\t{constraintName}");
                }
                return;
            }

            if (listen)
            {
                int port = 4545;
                if (!string.IsNullOrWhiteSpace(portStr))
                {
                    port = int.Parse(portStr);
                }
                using WebsocketListener websocketListener = new();
                await websocketListener.Listen("localhost", port);

                Console.WriteLine("Press CTRL + Q to quit.");

                while (true)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.Q)
                    {
                        return;
                    }
                }
            }

            bool haveFPuzzlesURL = !string.IsNullOrWhiteSpace(fpuzzlesURL);
            bool haveGivens = !string.IsNullOrWhiteSpace(givens);
            bool haveBlankGridSize = !string.IsNullOrWhiteSpace(blankGridSizeString);
            bool haveCandidates = !string.IsNullOrWhiteSpace(candidates);
            if (!haveFPuzzlesURL && !haveGivens && !haveBlankGridSize && !haveCandidates)
            {
                Console.WriteLine($"ERROR: Must provide either an f-puzzles URL or a givens string or a blank grid or a candidates string.");
                Console.WriteLine($"Try '{processName} --help' for more information.");
                showHelp = true;
            }

            int numBoardsSpecified = 0;
            if (haveFPuzzlesURL)
            {
                numBoardsSpecified++;
            }
            if (haveGivens)
            {
                numBoardsSpecified++;
            }
            if (haveBlankGridSize)
            {
                numBoardsSpecified++;
            }
            if (haveCandidates)
            {
                numBoardsSpecified++;
            }

            if (numBoardsSpecified != 1)
            {
                Console.WriteLine($"ERROR: Cannot provide more than one set of givens (f-puzzles URL, given string, blank grid, candidates).");
                Console.WriteLine($"Try '{processName} --help' for more information.");
                return;
            }

            Solver solver;
            try
            {
                if (haveBlankGridSize)
                {
                    if (int.TryParse(blankGridSizeString, out int blankGridSize) && blankGridSize > 0 && blankGridSize < 32)
                    {
                        solver = SolverFactory.CreateBlank(blankGridSize, constraints);
                    }
                    else
                    {
                        Console.WriteLine($"ERROR: Blank grid size must be between 1 and 31");
                        Console.WriteLine($"Try '{processName} --help' for more information.");
                        return;
                    }
                }
                else if (haveGivens)
                {
                    solver = SolverFactory.CreateFromGivens(givens, constraints);
                }
                else if (haveFPuzzlesURL)
                {
                    solver = SolverFactory.CreateFromFPuzzles(fpuzzlesURL, constraints);
                    Console.WriteLine($"Imported \"{solver.Title ?? "Untitled"}\" by {solver.Author ?? "Unknown"}");
                }
                else // if (haveCandidates)
                {
                    solver = SolverFactory.CreateFromCandidates(candidates, constraints);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return;
            }

            if (print)
            {
                Console.WriteLine("Input puzzle:");
                solver.Print();
            }

            if (solveLogically)
            {
                Console.WriteLine("Solving logically:");
                List<LogicalStepDesc> logicalStepDescs = new();
                var logicResult = solver.ConsolidateBoard(logicalStepDescs);
                foreach (var step in logicalStepDescs)
                {
                    Console.WriteLine(step.ToString());
                }
                if (logicResult == LogicResult.Invalid)
                {
                    Console.WriteLine($"Board is invalid!");
                }
                solver.Print();

                if (outputPath != null)
                {
                    try
                    {
                        using StreamWriter file = new(outputPath);
                        await file.WriteLineAsync(solver.OutputString);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to write to file: {e.Message}");
                    }
                }

                if (fpuzzlesOut)
                {
                    OpenFPuzzles(solver, visitURL);
                }
            }

            if (solveBruteForce)
            {
                Console.WriteLine("Finding a solution with brute force:");
                if (!solver.FindSolution(multiThread: multiThread))
                {
                    Console.WriteLine($"No solutions found!");
                }
                else
                {
                    solver.Print();

                    if (outputPath != null)
                    {
                        try
                        {
                            using StreamWriter file = new(outputPath);
                            await file.WriteLineAsync(solver.OutputString);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Failed to write to file: {e.Message}");
                        }
                    }

                    if (fpuzzlesOut)
                    {
                        OpenFPuzzles(solver, visitURL);
                    }
                }
            }

            if (solveRandomBruteForce)
            {
                Console.WriteLine("Finding a random solution with brute force:");
                if (!solver.FindSolution(multiThread: multiThread, isRandom: true))
                {
                    Console.WriteLine($"No solutions found!");
                }
                else
                {
                    solver.Print();

                    if (outputPath != null)
                    {
                        try
                        {
                            using StreamWriter file = new(outputPath);
                            await file.WriteLineAsync(solver.OutputString);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Failed to write to file: {e.Message}");
                        }
                    }

                    if (fpuzzlesOut)
                    {
                        OpenFPuzzles(solver, visitURL);
                    }
                }
            }

            if (trueCandidates)
            {
                Console.WriteLine("Finding true candidates:");
                int currentLineCursor = Console.CursorTop;
                object consoleLock = new();
                if (!solver.FillRealCandidates(multiThread: multiThread, progressEvent: (uint[] board) =>
                {
                    uint[,] board2d = new uint[solver.HEIGHT, solver.WIDTH];
                    for (int i = 0; i < solver.HEIGHT; i++)
                    {
                        for (int j = 0; j < solver.WIDTH; j++)
                        {
                            int cellIndex = i * solver.WIDTH + j;
                            board2d[i, j] = board[cellIndex];
                        }
                    }
                    lock (consoleLock)
                    {
                        ConsoleUtility.PrintBoard(board2d, solver.Regions, Console.Out);
                        Console.SetCursorPosition(0, currentLineCursor);
                    }
                }))
                {
                    Console.WriteLine($"No solutions found!");
                }
                else
                {
                    solver.Print();

                    if (outputPath != null)
                    {
                        try
                        {
                            using StreamWriter file = new(outputPath);
                            await file.WriteLineAsync(solver.OutputString);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Failed to write to file: {e.Message}");
                        }
                    }

                    if (fpuzzlesOut)
                    {
                        OpenFPuzzles(solver, visitURL);
                    }
                }
            }

            if (solutionCount)
            {
                Console.WriteLine("Finding solution count...");

                try
                {
                    Action<Solver> solutionEvent = null;
                    using StreamWriter file = (outputPath != null) ? new(outputPath) : null;
                    if (file != null)
                    {
                        solutionEvent = (Solver solver) =>
                        {
                            try
                            {
                                file.WriteLine(solver.GivenString);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"Failed to write to file: {e.Message}");
                            }
                        };
                    }

                    ulong numSolutions = solver.CountSolutions(maxSolutions: maxSolutionCount, multiThread: multiThread, progressEvent: (ulong count) =>
                    {
                        ReplaceLine($"(In progress) Found {count} solutions in {watch.Elapsed}.");
                    },
                    solutionEvent: solutionEvent);

                    if (maxSolutionCount > 0)
                    {
                        numSolutions = Math.Min(numSolutions, maxSolutionCount);
                    }

                    if (maxSolutionCount == 0 || numSolutions < maxSolutionCount)
                    {
                        ReplaceLine($"\rThere are exactly {numSolutions} solutions.");
                    }
                    else
                    {
                        ReplaceLine($"\rThere are at least {numSolutions} solutions.");
                    }
                    Console.WriteLine();

                    if (file != null && sortSolutionCount)
                    {
                        Console.WriteLine("Sorting...");
                        file.Close();

                        string[] lines = await File.ReadAllLinesAsync(outputPath);
                        Array.Sort(lines);
                        await File.WriteAllLinesAsync(outputPath, lines);
                        Console.WriteLine("Done.");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"ERROR: {e.Message}");
                }
            }

            if (check)
            {
                Console.WriteLine("Checking...");
                ulong numSolutions = solver.CountSolutions(2, multiThread);
                Console.WriteLine($"There are {(numSolutions <= 1 ? numSolutions.ToString() : "multiple")} solutions.");
            }

            watch.Stop();
            Console.WriteLine($"Took {watch.Elapsed}");
        }

        private static void ReplaceLine(string text) =>
            Console.Write("\r" + text + new string(' ', Console.WindowWidth - text.Length) + "\r");

        private static void OpenFPuzzles(Solver solver, bool visit)
        {
            string url = SolverFactory.ToFPuzzlesURL(solver);
            Console.WriteLine(url);
            if (visit)
            {
                try
                {
                    OpenUrl(url);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Cannot open URL: {e}");
                }
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
