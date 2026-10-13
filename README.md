# Captain Tsubasa — Holly e Benji: nomi italiani

Mod offline per **Captain Tsubasa: Rise of New Champions**: **nomi con le versioni del cartone animato in Italia**.

La versione **1.3** comprende 31 corrispondenze di giocatori, 9 nomi di squadra e 7 allenatori o membri dello staff. Aggiorna nomi completi, nomi brevi, dialoghi, schede e descrizioni, mantenendo la lingua italiana del gioco.

## Download e installazione

Scarica l'installatore dalla [release più recente](https://github.com/jshodanVR/captain-tsubasa-nomi-italiani/releases/latest). Se scegli lo ZIP, estrailo prima di aprire il programma.

1. Installa il gioco da Steam e chiudilo.
2. Apri **Holly-e-Benji-Nomi-Italia-Installer-v1.3.exe**.
3. Controlla la cartella trovata; **Sfoglia** consente di selezionare il gioco in un'altra libreria o su un altro disco.
4. Conferma l'uso offline e premi **Installa per l'offline**. Autorizza Windows se richiede l'accesso amministratore.
5. **Metti Steam in modalità offline** e avvia il gioco in italiano.

È un installatore autonomo: per usarlo non servono Python, i vecchi script o copie dei file originali di un altro PC. Controlla la versione del gioco, ricostruisce la patch e conserva gli originali prima della sostituzione.

## Roberto e gli allenatori

- Roberto Hongo → **Roberto Sedinho**.
- Tatsuo Mikami → **Freddy Marshall**.
- Kozo Kira → **Jeff Turner**.
- Makoto Kitazume → **Ray Thompson**.
- Takeshi Furuoya → **Jonathan Gunnell**.
- Munemasa Katagiri → **Kirk Parson**.
- Tadashi Shiroyama → **Peter Colby**, nei riferimenti presenti nei dialoghi.

Le corrispondenze complete sono in [data/nomi.json](data/nomi.json). **Koji Nishio → Jill Taylor** segue la scelta richiesta per questa mod.

I nomi variano talvolta fra edizioni e doppiaggi. I riferimenti consultati sono raccolti in [data/fonti-nomi.json](data/fonti-nomi.json). I personaggi privi di una corrispondenza verificata mantengono il nome presente nel gioco.

## Ripristino e aggiornamento

Chiudi il gioco, riapri l'installatore e premi **Ripristina originali**. Il ripristino comprende sia i testi sia il programma di avvio Easy Anti-Cheat originale, prima di tornare all'uso online standard.

Le copie originali restano in **GameData/Holly-e-Benji-Nomi-Italia-Backup**. Conserva questa cartella. Non vengono sovrascritte con file modificati.

La versione 1.3 riconosce le patch 1.1 e 1.2 e può aggiornarle se trova entrambe le copie originali nel gioco o nelle cartelle di backup accanto all'installatore. Se le copie non sono disponibili, chiede di ripristinare prima con il pacchetto precedente.

## Compatibilità e contenuto

- **Windows, edizione Steam, build 10135064**, con file verificati tramite SHA-256.
- **Uso esclusivamente offline**: l'attivazione usa una copia dell'eseguibile già installato al posto dell'avvio Easy Anti-Cheat. Il ripristino riporta entrambi i file agli originali.
- Non modifica salvataggi, parametri dei giocatori o regole di gioco.
- Riguarda i testi: audio giapponese, filmati e nomi disegnati nelle immagini restano quelli originali.
- **Meiwa → Muppet**; **Meiwa Higashi** resta distinta. **Otomo** e **Hirado** hanno già i nomi richiesti.
- **Ichiki / Hiromichi Ichiki** non è stato trovato nei testi. Akira Ichinose resta invariato.

Il repository contiene sorgenti e dati della patch. I salvataggi personali, gli eseguibili originali e il file completo dei testi del gioco non sono inclusi.

## Compilazione

Su Windows con .NET Framework:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
```

L'installatore, lo ZIP e l'impronta SHA-256 vengono creati in **dist/**. Lo script controlla anche l'integrità dei dati incorporati.

Per rigenerare la patch da un proprio file originale non modificato, con Python 3:

```powershell
python scripts/build_patch.py --source "C:\percorso\GameData\string\WW\text.ttb"
```

La rigenerazione verifica tutti gli **808.210 record** della tabella, preservando struttura, identificativi, terminatori, formattazione e testi non modificati. Solo le etichette condivise in alfabeto latino vengono aggiornate anche nella tabella giapponese.

## Verifiche

I riepiloghi sono in [tests/patch-verification.json](tests/patch-verification.json) e [tests/installer-verification.json](tests/installer-verification.json). I test locali usano una copia del gioco per verificare installazione, riapplicazione, ripristino identico, riconoscimento di versioni incompatibili e aggiornamento da una patch precedente.

La procedura per eseguire il test con i propri originali è [tests/integration.ps1](tests/integration.ps1). I file di test completi restano locali e sono esclusi dal repository. Lo script di compilazione verifica il pacchetto incorporato anche senza file del gioco.

## Licenza

Il codice dell'installatore e degli strumenti è distribuito con [licenza MIT](LICENSE). Il gioco, i personaggi e i testi originali appartengono ai rispettivi titolari.
