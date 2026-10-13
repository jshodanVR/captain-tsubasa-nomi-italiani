"""Create a reversible delta patch for the installed RONC TTB0 text table."""
from pathlib import Path
from collections import Counter
import struct, re, json, hashlib, argparse

ROOT = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description='Build the Italian cartoon-name patch from the unmodified TTB0 text file.')
parser.add_argument('--source', required=True, type=Path, help='Original, unmodified GameData/string/WW/text.ttb')
parser.add_argument('--out', type=Path, default=ROOT / 'patch')
parser.add_argument('--audit', type=Path, help='Optional local audit directory; never commit this directory.')
args = parser.parse_args()
SOURCE = args.source
OUT = args.out
OUT.mkdir(parents=True, exist_ok=True)
original = SOURCE.read_bytes()
assert hashlib.sha256(original).hexdigest() == 'a3c888f7c6bf4c567878af6083fafee2b431bca46107289aeec125f9b75327d9', 'Unsupported original text file'
h = struct.unpack_from('<44I', original)
assert original[:4] == b'TTB0'
count, language_count, loc, pool = h[4], h[3], h[8], h[9]
stride = 4 + 16 * count
assert loc + language_count * stride == pool
languages = {original[loc+i*stride:loc+i*stride+4].decode('ascii'):loc+i*stride for i in range(language_count)}
assert 'itIT' in languages and 'jaJP' in languages

def records(data, language):
    base = languages[language]
    for i in range(count):
        pos = base + 4 + i*16
        off, size, meta, flag = struct.unpack_from('<4I', data, pos)
        assert pool + off + size <= len(data), (language, i, 'bounds')
        raw = data[pool+off:pool+off+size]
        assert size == 0 or raw[-1] == 0, (language, i, 'terminator')
        text = raw[:-1].decode('utf-8') if size else ''
        yield i, pos, off, size, meta, flag, text

italian = list(records(original, 'itIT'))
japanese = list(records(original, 'jaJP'))

# Surnames identify the roster entries even where the user's given name differs.
mapping_data = json.loads((ROOT / 'data/nomi.json').read_text(encoding='utf-8'))
players = [(p['originale'], p['italiano'], p['breve'], p['varianti']) for p in mapping_data['giocatori']]
coaches = [(p['originale'], p['italiano'], p['breve'], p['varianti']) for p in mapping_data['allenatori_e_staff']]
teams = [(p['originale'], p['italiano'], p['varianti']) for p in mapping_data['squadre']]
replacements = {}
owner = {}
def add(source, target, entity):
    replacements[source] = target
    owner[source] = entity

target_full = set()
for full, target, short, aliases in players + coaches:
    target_full.add(full)
    for name in [full] + aliases: add(name, target, full)
    surname = full.split()[-1]
    if surname != 'Tachibana': add(surname, short, full)
add('Tachibana', 'Derrick', 'Gemelli Tachibana')
add('M. Tachibana', 'James', 'Masao Tachibana')
add('K. Tachibana', 'Jason', 'Kazuo Tachibana')
# Standalone given names actually used for these characters in dialogue.
for source, target in [('Tsubasa','Holly'), ('Genzo','Benji'), ('Taro','Tom'), ('Kojiro','Mark'),
                       ('Hikaru','Philip'), ('Jun','Julian'), ('Masao','James'), ('Kazuo','Jason'),
                       ('Takeshi','Danny')]:
    add(source,target,'nome breve: '+source)
for source,target,aliases in teams:
    for name in [source] + aliases: add(name,target,source)
team_codes={'NKT':'NEW','FRN':'FLY','MSS':'MAM','HNW':'HOT','AZM':'ART'}
for source,target in team_codes.items(): add(source,target,'sigla squadra: '+source)

# Longer protected phrases take precedence over the name substitutions.
protected = {'Captain Tsubasa', 'CAPTAIN TSUBASA', 'Meiwa Higashi',
             'Sano (Ryo)', 'Oda (Tsuyoshi)', 'R. Sano', 'K. Soda', 'T. Oda',
             'Azumaichi (Kazuaki)'}
for row in italian[44030:44356] + italian[44750:44785] + italian[44980:45000] + italian[45356:45387]:
    name = row[-1]
    if len(name.split()) >= 2 and name not in target_full and len(name) < 40 and '\n' not in name:
        protected.add(name)
protected.add('Shingo "Non Stop" Aoi')

def pattern(s):
    return re.escape(s).replace(r'\ ', r'\s+')
tokens = sorted(set(replacements) | protected, key=lambda s:(len(s.split()),len(s)), reverse=True)
rx = re.compile(r'(?<!\w)(?:' + '|'.join(pattern(s) for s in tokens) + r')(?!\w)', re.I)
lookup = {re.sub(r'\s+', ' ',s).casefold():s for s in tokens}
hits = Counter()

def convert(text, index):
    def sub(match):
        raw = match.group()
        key = lookup[re.sub(r'\s+', ' ',raw).casefold()]
        if key in protected: return raw
        # The second Soda in the roster is Kazuaki Soda, outside the supplied mapping.
        if index == 44544 and key == 'Soda': return raw
        target = replacements[key]
        if raw.isupper(): target = target.upper()
        if '\n' in raw and len(raw.split()) == len(target.split()):
            parts = re.split(r'(\s+)', raw)
            words = iter(target.split())
            target = ''.join(next(words) if k%2 == 0 else x for k,x in enumerate(parts))
        if target != raw: hits[owner[key]] += 1
        return target
    # Formatting tags can include identifiers; do not edit tag contents.
    pieces = re.split(r'(<[^>]*>|Captain\s+Tsubasa)', text, flags=re.I)
    return ''.join(part if k%2 else rx.sub(sub,part) for k,part in enumerate(pieces))

patched = bytearray(original)
appended = bytearray()
patches = []
changes = []
seen = {}
def rewrite(language, row, new_text):
    i,pos,off,size,meta,flag,text=row
    raw=new_text.encode('utf-8')+b'\0'
    if raw in seen:
        new_offset=seen[raw]
    else:
        new_offset=len(original)+len(appended)-pool
        seen[raw]=new_offset
        appended.extend(raw)
    struct.pack_into('<2I',patched,pos,new_offset,len(raw))
    patches.append((pos,off,size,new_offset,len(raw)))
    changes.append({'language':language,'index':i,'before':text,'after':new_text})

for row in italian:
    new_text=convert(row[-1],row[0])
    if new_text != row[-1]: rewrite('itIT',row,new_text)

# The Japanese table also holds the game's shared Roman alphabet labels.
# Only exact roster/team labels are changed there; Japanese prose stays intact.
shared = {full:target for full,target,_,_ in players + coaches}
shared.update({source:target for source,target,_ in teams})
shared.update(team_codes)
shared['Azuma Dai-ichi']='Artic'
for row in japanese:
    if row[-1] in shared and shared[row[-1]] != row[-1]: rewrite('jaJP',row,shared[row[-1]])
patched.extend(appended)

# Validate every table entry, including all untouched languages.
changed_positions={p[0] for p in patches}
for language in languages:
    for before,after in zip(records(original,language), records(patched,language)):
        assert before[0] == after[0]
        if before[1] not in changed_positions: assert before == after, (language,before[0],'unexpected change')
        else:
            assert before[4:6] == after[4:6], ('metadata modified',before[0])
            assert re.findall(r'<[^>]*>',before[-1]) == re.findall(r'<[^>]*>',after[-1])
assert patched[:loc] == original[:loc], 'header/main lookup tables changed'
assert patched[pool:len(original)] == original[pool:], 'original pool changed'
for pos,off,size,new_offset,new_size in patches:
    assert original[pos+8:pos+16] == patched[pos+8:pos+16]
    assert patched[pool+new_offset:pool+new_offset+new_size].endswith(b'\0')

by_index={row[0]:row[-1] for row in records(patched,'itIT')}
expected={44030:'Oliver Hutton',44031:'Mark Lenders',44032:'Benjamin Price',44033:'Tom Becker',
          44034:'Philip Callaghan',44035:'Julian Ross',44036:'James Derrick',44037:'Jason Derrick',
          44038:'Patrick Everett',44039:'Clifford Yuma',44040:'Ralph Peterson',44041:'Bruce Harper',
          44042:'Ed Warner',44043:'Alan Crocker',44044:'Bob Denver',44045:'Paul Diamond',
          44046:'Danny Mellow',44047:'Sandy Winters',44048:'Ted Carter',44049:'Gazza',
          44050:'Johnny Mason',44051:'Jack Morris',44052:'Charlie Custer',44054:'Jill Taylor',
          44055:'Oda',44056:'Honma',44057:'Sanada',44096:'Arthur Foster',44111:'Kato',44153:'Daimaru',
          45390:'Theodore Sellers',37645:'New Team',37646:'Toho Academy',37647:'Flynet',
          37648:'Mambo',37650:'Hot Dog',37651:'Artic',37653:'Meiwa Higashi',44544:'Soda',
          44127:'Ryoichi Sano',44138:'Kazuaki Soda',44339:'Jonathan Gunnell',44533:'Sano (Ryo)',
          44952:'R. Sano',44963:'K. Soda',44053:'Masao Nakayama',59315:'Captain Tsubasa',
          60428:italian[60428][-1],45252:'NEW',45254:'FLY',45255:'MAM',45257:'HOT',45258:'ART',
          44335:'Roberto Sedinho',44336:'Kirk Parson',44337:'Freddy Marshall',44338:'Jeff Turner',44340:'Ray Thompson'}
for i,value in expected.items(): assert by_index[i] == value, (i,by_index[i],value)
for full,target,short,aliases in players:
    assert any(row[-1] == full for row in italian), ('missing target',full)
    assert not any(re.search(r'(?<!\w)'+pattern(full)+r'(?!\w)',row[-1]) for row in records(patched,'itIT')), ('unconverted full name',full)
for full,target,short,aliases in coaches:
    surname = full.split()[-1]
    assert any(re.search(r'\b'+re.escape(surname)+r'\b',row[-1]) for row in italian), ('missing coach',full)
    assert not any(re.search(r'\b'+re.escape(surname)+r'\b',row[-1]) for row in records(patched,'itIT')), ('unconverted coach',full)
assert by_index[29772] == 'Chi è il mister Colby?'
for i,text in by_index.items():
    assert convert(text,i) == text, ('not idempotent',i,text)

sha=lambda data:hashlib.sha256(data).hexdigest()
(OUT/'append-text.bin').write_bytes(appended)
(OUT/'table-patch.bin').write_bytes(b''.join(struct.pack('<5I',*p) for p in patches))
manifest={'version':'1.3','game':'Captain Tsubasa: Rise of New Champions','steamAppId':1163550,
          'steamBuildId':'10135064','date':'2026-10-14','relativePath':'string/WW/text.ttb',
          'description':'nomi con le versioni del cartone animato in Italia',
          'originalSize':len(original),'patchedSize':len(patched),
          'originalSHA256':sha(original),'patchedSHA256':sha(patched),
          'appendSHA256':sha(appended),'tablePatchSHA256':sha((OUT/'table-patch.bin').read_bytes()),
          'patchCount':len(patches),'italianStringsChanged':sum(c['language']=='itIT' for c in changes),
          'sharedRomanLabelsChanged':sum(c['language']=='jaJP' for c in changes),
          'playerMappings':len(players),'coachMappings':len(coaches),'teamMappings':len(teams),
          'verifiedEntries':count*language_count,'nishio':'Jill Taylor',
          'notFound':['Hiromichi Ichiki / Ichiki'], 'preservedTeams':['Meiwa Higashi','Otomo','Hirado'],
          'requiresOffline':True,
          'originalLauncherSHA256':'a513262406467634cbfc836822156bc2427182bb0a5edc1b04b887bdd88fe6ee',
          'mainExecutableSHA256':'fc29f752c4688cc91594d8b32a389463281a8f8f6f17ecc7977cdfa64a7397c2',
          'previousPatchSHA256':['88afae16d8c5e07bb1fd4048576b2e4dbb14e3691c67f1e2343325390dbb4d34']}
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
summary = {'verifiedEntries':count*language_count,'rosterChecks':len(expected),
           'playerMappings':len(players),'coachMappings':len(coaches),'teamMappings':len(teams),
           'italianStringsChanged':manifest['italianStringsChanged'],
           'sharedRomanLabelsChanged':manifest['sharedRomanLabelsChanged'],
           'checks':['bounds','UTF-8','terminators','unchanged-language records','metadata preservation',
                     'formatting tags','original pool preservation','roster names','idempotence']}
(ROOT/'tests/patch-verification.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
if args.audit:
    args.audit.mkdir(parents=True,exist_ok=True)
    (args.audit/'text-patched.ttb').write_bytes(patched)
    (args.audit/'replacement-counts.json').write_text(json.dumps(hits,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(manifest,ensure_ascii=False,indent=2))
print('All structural, preservation, roster, placeholder, and idempotence checks passed.')
