"""Generate the NeuroQ - V1 CRM flow (draft) + Life Seasons catalog seed from the CRMPro sources.

Outputs:
  v1_seed.sql       — 4 products + 15 offers (variant SKUs as offer SKU overrides), custom field defs, the draft flow row (inactive)
  v1_flow.json      — the flow definition (also exported to docs/integrations)
  v1_review.txt     — every node's label + plain text, for a quick human read-through

Script text comes verbatim from the V1 RTF ScriptBoxes (rtf2html) with <* *> code blocks replaced
by {{variables}} or split into per-case nodes; see docs/integrations/neuroq-v1-crm-script.md."""
import json, os, re, sys, uuid

sys.path.insert(0, os.path.dirname(__file__))
from rtf2html import convert

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.join(HERE, 'res')
NS = uuid.UUID('a1b2c3d4-0164-4000-9000-000000000000')
def uid(name): return str(uuid.uuid5(NS, name))

TENANT_SUB = 'test-tenant'
CLIENT = 'd1ed06db-660d-4f81-80df-decd6fcd5155'      # Life Seasons, LLC
CAMPAIGN = 'e0a76800-46f8-4257-98a0-dc84a399fb62'    # NeuroQ
CREATED_BY = '8eaf7c08-a5e5-43b9-ad98-cb3fe824f978'
FLOW_ID = uid('flow:neuroq-v1')
ORDER_ENDPOINT = 'f5ac2a4c-502c-4d4e-9ade-1932c2b29be5'   # Life Seasons Order API / Add Order
CF_CALL_TYPE = '115b7d9c-b5a5-4681-bc7e-27c0f7dc59fa'
CF_DISPOSITION = 'fe68b17e-0fb6-46bb-b5ab-daa6b12404d0'
CF_REASON = uid('cf:disposition_reason')
CF_SMS = uid('cf:sms_consent')

GREEN = '#008000'   # CRMPro's colour for dynamic text
BLUE = '#0000ff'    # agent directions


def dyn(var):
    return f'<span style="color: {GREEN}">{{{{{var}}}}}</span>'


def agent(text):
    return f'<p><strong><span style="color: {BLUE}">{text}</span></strong></p>'


# ── Catalog (from V1's $this.Offers + UpdateCart) ────────────────────────────
CATALOG = [
    # sku, description, price, s/h, autoship interval days (0 = one-time), cannella sku
    ('283-3-CTY-90', 'NeuroQ Memory & Focus Buy 2 Get 1 Free Every 3 Months', 139.90, 4.95, 90, 'B2G1 CTY 139.90'),
    ('283-6-CTY-90', 'NeuroQ Memory & Focus Couples Buy 4 Get 2 Free Every 3 Months', 209.85, 4.95, 90, 'FAMOFFER 209.85'),
    ('283-2-CTY-60', 'NeuroQ Memory & Focus 2 Bottles Every 2 Months', 99.90, 4.95, 60, 'B2 CTY 99.90'),
    ('283-2-CTY-60-P1', 'NeuroQ Memory & Focus 2 Bottles Every 2 Months with DHA', 99.90, 4.95, 60, 'B2 CTY 99.90 W MEM'),
    ('283-3-OT-1', 'NeuroQ Memory & Focus Buy 2 Get 1 Free', 179.90, 12.95, 0, 'B2G1 179.90'),
    ('326-1-CTY-90', 'Memory & Focus Buy 2 Get 1 Free + Matching DHA', 189.80, 4.95, 90, None),
    ('326-2-CTY-90', 'Memory & Focus Couples Buy 4 Get 2 Free + Matching DHA', 309.60, 4.95, 90, None),
    ('303-6-CTY-90', 'NeuroQ Memory DHA-400 6 Bottles Every 3 Months', 99.75, 0, 90, 'MEM 2X3 MTH 99.75'),
    ('303-3-CTY-90', 'NeuroQ Memory DHA-400 3 Bottles Every 3 Months', 49.90, 0, 90, 'MEM 49.90'),
    ('303-2-CTY-60', 'NeuroQ Memory DHA-400 2 Bottles Every 2 Months', 49.90, 0, 60, 'MEM 49.90'),
    ('303-1-OT-1', 'NeuroQ Memory DHA-400', 24.95, 0, 0, None),
    ('307-6-CTY-90', 'Sleep Now 6 Boxes Every 3 Months', 89.70, 0, 90, 'SLP 2X3 MTH 89.70'),
    ('307-3-CTY-90', 'Sleep Now 3 Boxes Every 3 Months', 49.90, 0, 90, 'SLP 3 MTH 49.90'),
    ('307-2-CTY-60', 'Sleep Now 2 Boxes Every 2 Months', 39.90, 0, 60, 'SLP 2 MTH 39.90'),
    ('307-1-OT-1', 'Sleep Now', 19.95, 0, 0, None),
]
OFFER = {sku: uid(f'offer:{sku}') for sku, *_ in CATALOG}
# Physical products (S169): one per product family; each catalog row is an offer on its family's product,
# carrying its variant SKU as the offer's SKU override.
FAMILIES = {
    '283': 'NeuroQ Memory & Focus',
    '326': 'NeuroQ Memory & Focus + Matching DHA',
    '303': 'NeuroQ Memory DHA-400',
    '307': 'NeuroQ Sleep Now',
}
DESC = {sku: d for sku, d, *_ in CATALOG}

# ── Script text ───────────────────────────────────────────────────────────────
SIMPLE = {
    'ResponseText = Script.txtFirstName.Text': dyn('flow.first_name'),
    'responsetext = Script.txtFirstName.Text': dyn('flow.first_name'),
    'ResponseText = Script.BillingAddress.FirstName': dyn('flow.billing_address.firstName'),
    'ResponseText = Script.BillingAddress.Address1': dyn('flow.billing_address.address1'),
    'ResponseText = Script.BillingAddress.Address2': dyn('flow.billing_address.address2'),
    'ResponseText = Script.BillingAddress.City': dyn('flow.billing_address.city'),
    'ResponseText = Script.BillingAddress.State': dyn('flow.billing_address.state'),
    'ResponseText = Script.BillingAddress.Zip': dyn('flow.billing_address.zip'),
}
PROBE_PHRASE = dyn('flow.probe_phrase')


def clean_code(c):
    c = re.sub(r'\\[a-z]+-?\d* ?', '', c)
    return re.sub(r'\s+', ' ', c).strip()


def script(ctrl, codes=None, key='ScriptBox'):
    """ScriptBox RTF → HTML. `codes` maps CODE index → replacement HTML (or a callable taking the
    code text); unmapped simple tags use SIMPLE; anything else is an error so nothing is dropped
    silently."""
    path = os.path.join(RES, f'{ctrl}.{key}.rtf') if key else os.path.join(RES, ctrl)
    html, tags = convert(open(path, encoding='utf-8').read())
    codes = codes or {}
    for i, t in enumerate(tags):
        c = clean_code(t)
        if i in codes:
            rep = codes[i](c) if callable(codes[i]) else codes[i]
        elif c in SIMPLE:
            rep = SIMPLE[c]
        elif 'cboProbe.SelectedItem' in c and "the issues you've been experiencing" in c:
            rep = PROBE_PHRASE
        else:
            raise ValueError(f'{ctrl}: unmapped code #{i}: {c[:160]}')
        html = html.replace(f'[[CODE{i}]]', rep)
    # RTF artefacts: the \* destination marker, symbol-font glyphs after ligatures
    html = re.sub(r'^<p>\*', '<p>', html)
    html = html.replace('*PrFont0Indent1440 ', '').replace('*PrFont0Indent1440', '')
    html = html.replace('\ufb00?', 'ff').replace('\ufb01?', 'fi').replace('\u2193</span></strong><strong><span style="color: #ff0000">?', '\u2193')
    html = re.sub(r'<span style="color: #191970">(.*?)</span>', r'\1', html)
    return html


# ── Flow builder ─────────────────────────────────────────────────────────────
nodes = {}
col_y = {}


def add(nid, typ, label, col, transitions=None, **fields):
    assert nid not in nodes, nid
    y = col_y.get(col, 0)
    col_y[col] = y + 170
    nodes[nid] = {'_pos': {'x': col * 380, 'y': y}, 'type': typ, 'label': label,
                  'transitions': transitions or {}, **fields}
    return nid


def select(nid, label, col, options, transitions, content, out, required=True, script_label=''):
    return add(nid, 'input', label, col, transitions, fieldType='select', required=required,
               options=[{'label': o, 'value': o} for o in options], scriptLabel=script_label,
               scriptContent=content, outputVariable=out, inputMask='', customMask='')


def text_input(nid, label, col, nxt, content, out, required=False, mask=''):
    return add(nid, 'input', label, col, {'default': nxt}, fieldType='text', required=required,
               scriptLabel='', scriptContent=content, outputVariable=out, inputMask=mask, customMask='')


def sv(nid, label, col, nxt, **assign):
    return add(nid, 'set_variable', label, col, {'default': nxt},
               assignments=[{'variable': k if k.startswith('{{') else f'{{{{flow.{k}}}}}', 'value': v}
                            for k, v in assign.items()])


def section(nid, name, col, nxt):
    return add(nid, 'section', name, col, {'default': nxt}, name=name, outputVariable=f'{nid}_section',
               allowJumpFromAnywhere=True, clearPreviousValues=False)


def branch(nid, label, col, cond, t, f):
    return add(nid, 'branch', label, col, {'true': t, 'false': f}, condition=cond)


def cart_add(nid, label, col, sku, nxt, mode='add', replaces=()):
    # Each add gets its own failure note right beside it (continuing to the same next step) —
    # one shared failure node reached from ~20 places made the canvas unreadable.
    add(f'{nid}_fail', 'script', f'{sku} could not be added', col, {'default': nxt},
        content=agent(f'AGENT: {DESC[sku]} could not be added to the cart (it may be out of stock). '
                      'Tell the caller it is unavailable today, let a supervisor know, then click Continue.'))
    return add(nid, 'add_to_cart', label, col, {'added': nxt, 'failed': f'{nid}_fail'},
               offerId=OFFER[sku], offerDisplayName=DESC[sku], quantity=1, mode=mode,
               replacesOfferIds=[OFFER[s] for s in replaces], replacesOfferNames=[DESC[s] for s in replaces])


def wrapup(prefix, col):
    """Save call type / disposition / reason, then end — one short chain per ending so no edge has
    to travel across the whole canvas to a single shared wrap-up."""
    custom(f'v1_{prefix}_cf1', 'Call Type', col, CF_CALL_TYPE, 'call_type', '{{flow.call_type}}', f'v1_{prefix}_cf2')
    custom(f'v1_{prefix}_cf2', 'Disposition', col, CF_DISPOSITION, 'disposition', '{{flow.disposition}}', f'v1_{prefix}_cf3')
    custom(f'v1_{prefix}_cf3', 'Disposition Reason', col, CF_REASON, 'disposition_reason', '{{flow.disposition_reason}}',
           f'v1_{prefix}_end')
    add(f'v1_{prefix}_end', 'end', 'End', col, status='complete')
    return f'v1_{prefix}_cf1'


def custom(nid, label, col, defid, field, value, nxt):
    return add(nid, 'set_custom_field', label, col, {'success': nxt, 'error': nxt, 'invalid_value': nxt},
               definitionId=defid, definitionFieldName=field, definitionDataTypeName='string',
               definitionDisplayLabel=label, value=value)


# Columns: 0 opening · 1-3 offer · 4 customer info · 5 payment · 6-8 upsells · 9-10 closing · 11 dispositions
PRICE_PANEL = script('RichTextBoxControl2.RichText.rtf', key=None)

# ─ Opening ─
add('v1_start', 'set_variable', 'Initialize', 0, {'default': 'v1_sec_opening'},
    assignments=[{'variable': '{{flow.auth_attempts}}', 'value': '0'},
                 {'variable': '{{flow.autoship_confirm}}', 'value': ''},
                 {'variable': '{{flow.autoship_note}}', 'value': ''}])
section('v1_sec_opening', 'Opening', 0, 'v1_first_name')
text_input('v1_first_name', 'First Name', 0, 'v1_set_bill_first',
           script('txtFirstName', {0: dyn('agent.first_name')}), 'first_name', required=True)
sv('v1_set_bill_first', 'Billing first name = first name', 0, 'v1_zip', **{'billing_address.firstName': '{{flow.first_name}}'})
text_input('v1_zip', 'Zip Code', 0, 'v1_set_bill_zip', script('txtZipCode'), 'zip_code', mask='__zip_ca__')
sv('v1_set_bill_zip', 'Billing zip = zip', 0, 'v1_call_type', **{'billing_address.zip': '{{flow.zip_code}}'})
CALL_TYPES = ['Order', 'All Other Calls', 'Junk (Test / Prank / Wrong Number / No One On Line)']
select('v1_call_type', 'Call Type', 0, CALL_TYPES,
       {'Order': 'v1_sec_offer', 'All Other Calls': 'v1_disp_aoc', CALL_TYPES[2]: 'v1_disp_junk'},
       script('cboCallType'), 'call_type', script_label='Opening')

# ─ Offer: probes ─
section('v1_sec_offer', 'Offer', 1, 'v1_probe1')
PROBES = ['Memory issues and forgetfulness', 'Struggling to recall things or learn new  things', 'Poor mental clarity',
          'Struggles with focus', 'Poor Concentration', 'Brain fog', 'Confusion', 'All the above', 'Something Else',
          'I just want the price']
GENERIC = {'Something Else', 'All the above', 'Has Questions', 'I just want the price'}
probe_tr = {}
for i, p in enumerate(PROBES):
    nid = f'v1_probe_set_{i}'
    probe_tr[p] = nid
    phrase = "the issues you've been experiencing" if p in GENERIC else 'your ' + ' '.join(p.lower().split())
    topic = 'brain health' if p in GENERIC else ' '.join(p.split())
    nxt = 'v1_price_rebuttal' if p == 'I just want the price' else 'v1_probe2'
    sv(nid, f'Probe: {p}', 2, nxt, probe_phrase=phrase, probe_topic=topic)
select('v1_probe1', 'Probe 1', 1, PROBES, probe_tr,
       script('cboProbe') + '<hr>' + PRICE_PANEL, 'probe1', script_label='Offer')
text_input('v1_probe2', 'Probe 2', 1, 'v1_probe3', script('txtProbe2'), 'probe2')
text_input('v1_probe3', 'Probe 3', 1, 'v1_probe4', script('txtProbe3'), 'probe3')
text_input('v1_probe4', 'Probe 4', 1, 'v1_hot_button', script('txtProbe4'), 'probe4')
select('v1_hot_button', 'Press Hot Button', 1, ['Continue'], {'Continue': 'v1_main_offer'},
       script('cboHotButton'), 'hot_button')

# ─ Offer: sales ladder ─
select('v1_main_offer', 'Main Offer', 1, ['Yes - Place Order', 'No - Does not want auto', 'No - Go to Rebuttal'],
       {'Yes - Place Order': 'v1_pkg_3mo', 'No - Does not want auto': 'v1_auto_rebuttal', 'No - Go to Rebuttal': 'v1_rebuttal'},
       script('cboMainOffer'), 'main_offer')
select('v1_price_rebuttal', 'Price Rebuttal', 1, ['Yes - Continue', 'No - Go to Rebuttal 1'],
       {'Yes - Continue': 'v1_pkg_3mo', 'No - Go to Rebuttal 1': 'v1_rebuttal'}, script('cboPriceRebuttal'), 'price_rebuttal')
select('v1_auto_rebuttal', 'Auto Rebuttal', 1, ['Yes', 'No - Go to Basic Offer with OTS'],
       {'Yes': 'v1_pkg_3mo', 'No - Go to Basic Offer with OTS': 'v1_basic_ots'}, script('cboAutoRebuttal'), 'auto_rebuttal')
select('v1_basic_ots', 'Basic offer with OTS', 1, ['Yes', 'No - Go to Rebuttal 2'],
       {'Yes': 'v1_pkg_ots', 'No - Go to Rebuttal 2': 'v1_downsell1'}, script('cboBasicwithOTS'), 'basic_with_ots')
select('v1_rebuttal', 'Rebuttal', 1, ['Yes', 'No - Go to Objections Then Downsell 1'],
       {'Yes': 'v1_pkg_3mo', 'No - Go to Objections Then Downsell 1': 'v1_objections'}, script('cboRebuttal'), 'rebuttal')
OBJECTIONS = ['Too expensive', 'Think it over / More info', 'No CC', 'Thought it was Free', 'Spouse',
              'MD (Need to ask doctor)', 'Skeptical - Not Sure it will Work']
select('v1_objections', 'Objections', 1, OBJECTIONS, {o: f'v1_objreb_{i}' for i, o in enumerate(OBJECTIONS)},
       script('cboObjections'), 'objection')

# Objection rebuttal: V1 renders one Select Case; here one node per objection (verbatim texts).
OBJ_TEXT = {
    'Too expensive': f"I understand your hesitation, and earlier in the call you mentioned you have been dealing with {dyn('flow.probe_topic')} for {dyn('flow.probe4')} and that is impacting your life and things that make you happy. If you start taking NeuroQ and get those key brain nutrients working to help with your mental clarity and it would be well worth the investment, wouldn't it?! You're making a great decision for your brain health, what type of Credit Card will you be using today?",
    'Think it over / More info': f"I understand where you're coming from, and the most important thing is getting NeuroQ working in your brain to help slow the mental decline and begin to improve your brain function so we begin to address your {dyn('flow.probe_topic')}. The only way to do that is to just get started. You\u2019re making a great decision for your brain health, to what address would you like us to send your NeuroQ?",
    'No CC': 'I understand. Is it that it is too much up front, or you are not sure if it is worth the price?',
    'Thought it was Free': f"I understand {dyn('flow.first_name')} that your time is important, so let me re-cap one more time. Today with the TV Special Package, you'll get a FREE month and the 2 additional free gifts you saw on the show today. That's approx $100 in FREE bonuses with the TV Special offer! Sounds great, right? This buy 2 get one FREE Deal, a full 3-month supply of NeuroQ is only $139.90 + $4.95 S/H. AND you have 60 days to make sure it is right for you, Let's try this offer ok? What address would you like me to ship this to {dyn('flow.first_name')}?",
    'Spouse': "I understand. I'm sure you spouse will have two questions, how much is it, and does it work, right? You are getting a great discount today from the TV special, and the only way to find out if it works is to try it out. SO, let's get this right out to you. What address would you like us to send it to?",
    'MD (Need to ask doctor)': 'I understand, a doctor\u2019s seal of approval is important, that\'s why NeuroQ was developed by a doctor. But not just any doctor, a neurologist, and one of the world\u2019s top experts on the aging brain.  If you have a concern then just bring  the actual bottles and show your doctor or pharmacist the supplement facts and ingredient listing. If they understand supplements, then they should recognize all the natural ingredients that work with your body much like good nutrition. You\u2019ve got the 60-day money back guarantee. So, let\u2019s get this right out to you. What address would you like us to send it?',
    'Skeptical - Not Sure it will Work': 'I understand, it\u2019s good to be skeptical when it comes to your health, whether it\'s a supplement or a drug. But NeuroQ is different, as you saw in the commercial. It underwent a large-scale clinical trial confirming its safety and efficacy. It\u2019s also doctor-developed, by a neurologist who is considered one of the world\u2019s experts on brain aging and brain health. We have tens of thousands of people around the world that rely on NeuroQ. We wouldn\u2019t be able to be on TV making these claims if it wasn\u2019t effective. And NeuroQ is easy to take too. You\u2019re making a great decision, a critical one actually, for your brain health as you age, what address would you like us to send your NeuroQ to?',
}
# ('Too expensive' falls back to "too long" when Probe 4 is blank — set below.)
for i, o in enumerate(OBJECTIONS):
    select(f'v1_objreb_{i}', f'Objection Rebuttal: {o}', 2, ['Yes - Place Order - B2G1', 'No - Go to Downsell 1'],
           {'Yes - Place Order - B2G1': 'v1_pkg_3mo', 'No - Go to Downsell 1': 'v1_downsell1'},
           f'<p>"{OBJ_TEXT[o]}"</p>', 'objection_rebuttal')
select('v1_downsell1', 'Downsell 1', 1, ['Yes - Place Order', 'No - Go to Rebuttal for Downsell 1'],
       {'Yes - Place Order': 'v1_pkg_2mo', 'No - Go to Rebuttal for Downsell 1': 'v1_rebuttal1'}, script('cboDownsell1'), 'downsell1')
select('v1_rebuttal1', 'Rebuttal for Downsell 1', 1, ['Yes - Place Order', 'No - Go to Rebuttal 2'],
       {'Yes - Place Order': 'v1_pkg_2mo_dha', 'No - Go to Rebuttal 2': 'v1_rebuttal2'}, script('cboRebuttal1'), 'rebuttal1')
R2 = ['Yes - 3 Month Package', 'Yes - 2 Month Package', 'Yes - 2 Month Package + 1 Free One-Time DHA', 'No - Log Call']
select('v1_rebuttal2', 'Rebuttal 2', 1, R2,
       {R2[0]: 'v1_pkg_3mo', R2[1]: 'v1_pkg_2mo', R2[2]: 'v1_pkg_2mo_dha', R2[3]: 'v1_call_type'},
       script('cboRebuttal2'), 'rebuttal2')

# ─ Packages: reset the cart, add the base package, remember the tier ─
AUTOSHIP_CONFIRM = ('Also, to confirm, by taking advantage of our preferred customer feature, we will ship you a brand new '
                    'shipment in {f} months and every {f} months thereafter at the same discounted pricing charged to the '
                    'same card you are using today. You can cancel shipments at any time by calling 1-833-638-7674 OK?')
SUPPLY = {'3mo': ('90 days you\u2019ll get a fresh 90 day supply \u2013 at that same great price of $139.90 with $4.95 S/H today and on all future shipments', 3),
          '2mo': ('60 days you\u2019ll get a fresh 60 day supply \u2013 at that same great of $99.90 with $4.95 S/H today and on all future shipments', 2)}
for pkg, sku, tier in [('3mo', '283-3-CTY-90', '3mo'), ('2mo', '283-2-CTY-60', '2mo'),
                       ('2mo_dha', '283-2-CTY-60-P1', '2mo'), ('ots', '283-3-OT-1', 'ots')]:
    add(f'v1_pkg_{pkg}', 'reset_cart', f'Package: {DESC[sku]}', 3, {'default': f'v1_pkg_{pkg}_add'})
    cart_add(f'v1_pkg_{pkg}_add', f'Add {sku}', 3, sku, f'v1_pkg_{pkg}_vars')
    if tier == 'ots':
        sv(f'v1_pkg_{pkg}_vars', 'Package vars (one-time)', 3, 'v1_sec_customer', pkg_tier='ots', autoship_supply='',
           autoship_confirm='', autoship_note='')
    else:
        supply, freq = SUPPLY[tier]
        sv(f'v1_pkg_{pkg}_vars', f'Package vars ({tier})', 3, 'v1_sec_customer', pkg_tier=tier, autoship_supply=supply,
           autoship_confirm=AUTOSHIP_CONFIRM.format(f=freq), autoship_note=' (Must get clear Yes or Ok)')

# ─ Customer Info ─
section('v1_sec_customer', 'Customer Info', 4, 'v1_bill_addr')
add('v1_bill_addr', 'address', 'Billing Address', 4, {'default': 'v1_bill_phone'}, addressRole='billing',
    scriptLabel='', scriptContent='', outputVariable='billing_address', useValidation=True, allowInternational=False,
    showMiddleInitial=True, showCompany=False, requiredFields=['firstName', 'lastName', 'address1', 'zip', 'city', 'state'],
    fieldScripts={
        'firstName': script('BillingAddress.FirstNameScript.rtf', {0: 'May I have your first name, please?'}, key=None),
        'lastName': script('BillingAddress.LastNameScript.rtf', key=None),
        'address1': script('BillingAddress.Address1PrefixScript.rtf',
                           {0: 'May I have your billing address please?',
                            1: '(AGENT: The address must be the same as it shows on the check or the credit card statement.)'}, key=None),
        'city': script('BillingAddress.CityScript.rtf',
                       {0: dyn('flow.billing_address.city') + ', ' + dyn('flow.billing_address.state')}, key=None),
    })
add('v1_bill_phone', 'phone', 'Billing Phone', 4, {'default': 'v1_ship_same'}, phoneRole='billing', required=True,
    dncCheck=False, allowInternational=False, scriptLabel='', outputVariable='billing_phone',
    scriptContent='<p>"May I have your phone number, please?"</p>')
select('v1_ship_same', 'Shipping same as billing?', 4, ['Yes', 'No'], {'Yes': 'v1_ship_copy', 'No': 'v1_ship_addr'},
       script('ShippingAddress.SameAsBillingScript.rtf', key=None), 'ship_same_as_billing')
sv('v1_ship_copy', 'Shipping = billing', 4, 'v1_email', **{'{{call_record.shipping_address}}': '{{flow.billing_address}}',
                                                         'shipping_address': '{{flow.billing_address}}'})
add('v1_ship_addr', 'address', 'Shipping Address', 5, {'default': 'v1_canada'}, addressRole='shipping',
    scriptLabel='', scriptContent='<p>"And what address will we be shipping your order to?"</p>', outputVariable='shipping_address',
    useValidation=True, allowInternational=True, showMiddleInitial=False, showCompany=False,
    requiredFields=['firstName', 'lastName', 'address1', 'zip', 'city', 'state'],
    fieldScripts={'city': script('ShippingAddress.CityScript.rtf',
                                 {0: dyn('flow.shipping_address.city') + ', ' + dyn('flow.shipping_address.state')}, key=None)})
branch('v1_canada', 'Shipping to Canada?', 5, '{{flow.shipping_address.isCanada}} == "true"', 'v1_canada_disp', 'v1_email')
sv('v1_canada_disp', 'Canada → All Other Calls / Shipping to Canada', 5, 'v1_disp_close_canada',
   call_type='All Other Calls', disposition='Shipping to Canada')
add('v1_email', 'email', 'Email Address', 4, {'default': 'v1_sec_payment'}, required=False, saveToCallRecord=True,
    checkMX=True, checkARecord=False, checkDisposable=True, scriptLabel='', outputVariable='customer_email',
    scriptContent=script('txtEmail'))

# ─ Payment: keypad secure capture (replaces V1's on-screen card entry — Stephen, S164) ─
section('v1_sec_payment', 'Payment', 5, 'v1_pay_intro')
add('v1_pay_intro', 'script', 'Payment — secure capture', 5, {'default': 'v1_cc_trigger'},
    content='<p>"Great! For your security, you\u2019ll enter your card details on your phone\u2019s keypad \u2014 I won\u2019t see or hear them. '
            'You\u2019ll hear a short prompt for your card number, then the expiration date, then the security code. Ready?"</p>'
            + agent('AGENT: Click Continue to start the secure capture. You\u2019ll be on hold music while the caller enters the card; '
                    'their progress shows in your softphone panel.'))
add('v1_cc_trigger', 'trigger_telephony_event', 'Start secure card capture', 5, {'default': 'v1_cc_wait'}, eventName='cc_capture')
add('v1_cc_wait', 'script', 'Wait for secure capture', 5, {'default': 'v1_cc_ok'},
    content=agent('AGENT: Wait for the secure capture to finish, then click Continue.'),
    waitForTelephonyEventName='cc_capture', waitForTelephonyEventTimeoutSeconds=180)
branch('v1_cc_ok', 'Card captured?', 5, '{{shared.CC_Capture_Success}} == true', 'v1_after_payment', 'v1_cc_retry')
select('v1_cc_retry', 'Card capture failed', 6, ['Yes', 'No'], {'Yes': 'v1_cc_trigger', 'No': 'v1_no_mop'},
       '<p>"I see you had some difficulty entering your payment information. Would you like to try again?"</p>', 'retry_cc_capture')
sv('v1_no_mop', 'No method of payment', 6, 'v1_disp_close_aoc', call_type='All Other Calls',
   disposition='No MOP (No Method of Payment)')
branch('v1_after_payment', 'One-time (basic with OTS)?', 5, '{{flow.pkg_tier}} == "ots"', 'v1_sec_closing', 'v1_sec_upsell')

# ─ Upsells ─
section('v1_sec_upsell', 'Upsell', 6, 'v1_autoship')
select('v1_autoship', 'Preferred Customer (AutoShip)', 6, ['Yes - Preferred Customer - Go to Family Offer', 'Not Interested'],
       {'Yes - Preferred Customer - Go to Family Offer': 'v1_as_yes', 'Not Interested': 'v1_autoship_rb'},
       script('cboAutoShip', {1: dyn('flow.autoship_supply')}), 'autoship')
branch('v1_as_yes', '2-bottle package?', 6, '{{flow.pkg_tier}} == "2mo"', 'v1_dha_2mo', 'v1_family')
select('v1_autoship_rb', 'Preferred Customer Rebuttal', 6,
       ['Yes - Preferred Customer - Go to Family Offer', 'Not Interested - Go to One Time Offer'],
       {'Yes - Preferred Customer - Go to Family Offer': 'v1_as_yes', 'Not Interested - Go to One Time Offer': 'v1_as_rb2_route'},
       script('cboAutoShipRb'), 'autoship_rebuttal')
branch('v1_as_rb2_route', 'Downsell package?', 6, '{{flow.pkg_tier}} == "2mo"', 'v1_autoship_rb2b', 'v1_autoship_rb2a')
select('v1_autoship_rb2a', 'Preferred Customer Rebuttal 2a', 7,
       ['Yes - Preferred Customer - Go to Family Offer', 'Not Interested - Go to One Time Offer'],
       {'Yes - Preferred Customer - Go to Family Offer': 'v1_as_yes', 'Not Interested - Go to One Time Offer': 'v1_onetime'},
       script('cboAutoShipRb2a'), 'autoship_rebuttal_2a')
select('v1_autoship_rb2b', 'Preferred Customer Rebuttal 2b', 7,
       ['Yes - Go to Memory DHA cross-sell', 'Not Interested - Go to One Time Offer'],
       {'Yes - Go to Memory DHA cross-sell': 'v1_dha_2mo', 'Not Interested - Go to One Time Offer': 'v1_onetime'},
       script('cboAutoShipRb2b'), 'autoship_rebuttal_2b')
ONETIME = 'Yes - 3 Month Supply $179.90 + $12.95 S/H ($192.85) - Go to Cross Sell 1'
select('v1_onetime', 'One Time Offer', 7, [ONETIME], {ONETIME: 'v1_ot_pkg'},
       script('cboOneTime', {1: 'TV Special 3-month supply package, with all the FREE gifts.',
                             2: 'AGENT: Total is $192.85 ($179.90 + $12.95 s/h)'}), 'one_time_offer')
add('v1_ot_pkg', 'reset_cart', 'Package: one-time', 7, {'default': 'v1_ot_add'})
cart_add('v1_ot_add', 'Add 283-3-OT-1', 7, '283-3-OT-1', 'v1_ot_vars')
sv('v1_ot_vars', 'Package vars (one-time)', 7, 'v1_sec_closing', pkg_tier='ots', autoship_confirm='', autoship_note='')
select('v1_family', 'Upsell - Matching Family Offer', 6, ['Yes - Go To Cross Sell', 'Not Interested'],
       {'Yes - Go To Cross Sell': 'v1_family_add', 'Not Interested': 'v1_dha_3mo'},
       script('cboUpsell'), 'family_offer')
cart_add('v1_family_add', 'Swap to 283-6-CTY-90 (family)', 6, '283-6-CTY-90', 'v1_family_vars', mode='replace',
         replaces=['283-3-CTY-90'])
sv('v1_family_vars', 'Package vars (family)', 6, 'v1_dha_family', pkg_tier='family',
   autoship_confirm=AUTOSHIP_CONFIRM.format(f=3).replace('a brand new shipment', 'a brand new shipment'))

# Matching Memory DHA (cboUpsell2) — text and offer depend on the base package.
DHA = {
    'family': ('Yes - Matching Memory DHA - 3*2 w/Auto $309.60', 'That\u2019s 5 more bottles to go with your free one today for a matching 6 bottle supply. It\u2019s just $99.75, saving you an additional $50 today, and with no additional shipping. And of course, all of our great products are covered by our rock solid 60-day money back guarantee, so you have nothing to lose! And that price is locked in on future shipments too. So let\u2019s go ahead and upgrade your order with a matching supply of Memory DHA, as the doctor recommends.',
               ('326-2-CTY-90', 'replace', ['283-6-CTY-90'])),
    '3mo': ('Yes - Matching Memory DHA - 3 w/Auto $189.90', 'That\u2019s 2 more bottles to go with your free one today for a matching 3-month supply. It\u2019s just $49.90, saving you another $10 today, and with no additional shipping. And of course, all of our great products are covered by our rock solid 60-day money back guarantee, so you have nothing to lose! And that price is locked in on future shipments too. So let\u2019s go ahead and upgrade your order with a matching supply of Memory DHA, as the doctor recommends.',
            ('326-1-CTY-90', 'replace', ['283-3-CTY-90'])),
    '2mo': ('Yes - Matching Memory DHA - 2 w/Auto $49.90', 'That\u2019s 2 bottles to go with your 2 bottles of NeuroQ. It\u2019s just $49.90, saving you another $10 today, and with no additional shipping. And of course, all of our great products are covered by our rock solid 60-day money back guarantee, so you have nothing to lose! And that price is locked in on future shipments too. So let\u2019s go ahead and upgrade your order with a matching supply of Memory DHA, as the doctor recommends.',
            ('303-2-CTY-60', 'add', [])),
}
dha_base = script('cboUpsell2', {2: '[[DHA_TEXT]]'})
SLEEP = {
    'family': ('Yes - Matching Sleep Now - 3*2 w/Auto $89.70', 'We have a great 40% off special going on a matching 6-month supply of Sleep Now, bringing the price down to just $14.95 a month for the doctor\u2019s premium sleep formula.  That\u2019s just $89.70, and with no additional shipping. And, of course, it\u2019s covered by our rock solid 60-day money back guarantee. So let\u2018s go ahead and add a matching supply of Sleep Now to your order for the best results. Ok?', '307-6-CTY-90'),
    '3mo': ('Yes - Matching Sleep Now - 3 w/Auto $49.90', 'We have a great buy 2 get 1 free special going on Sleep Now, so you\u2019ll get a matching 3-month supply at the discounted price of just $49.90, with no additional shipping. And, of course, it\u2019s covered by our rock solid 60-day money back guarantee. So let\u2018s go ahead and add a matching supply of Sleep Now to your order for the best results. Ok?', '307-3-CTY-90'),
    '2mo': ('Yes - Matching Sleep Now - 2 w/Auto $39.90', 'I can add a matching 2-month supply of Sleep Now at the discounted price of just $39.90, with no additional shipping, saving you an additional $10 today. And, of course, it\u2019s covered by our rock solid 60-day money back guarantee. So let\u2018s go ahead and add a matching supply of Sleep Now to your order for the best results. Ok?', '307-2-CTY-60'),
}
sleep_base = script('cboCrossSell2', {1: '[[SLEEP_TEXT]]'})
for i, tier in enumerate(['family', '3mo', '2mo']):
    opt, text, (sku, mode, repl) = DHA[tier]
    select(f'v1_dha_{tier}', f'Cross Sell 1 - Memory DHA ({tier})', 7 + (i > 0), [opt, 'Not Interested'],
           {opt: f'v1_dha_{tier}_add', 'Not Interested': f'v1_sleep_{tier}'},
           dha_base.replace('[[DHA_TEXT]]', text), 'dha_cross_sell')
    cart_add(f'v1_dha_{tier}_add', f'Add {sku}', 7 + (i > 0), sku, f'v1_sleep_{tier}', mode=mode, replaces=repl)
    sopt, stext, ssku = SLEEP[tier]
    select(f'v1_sleep_{tier}', f'Cross Sell 2 - Sleep Now ({tier})', 8, [sopt, 'Not Interested'],
           {sopt: f'v1_sleep_{tier}_add', 'Not Interested': 'v1_sec_closing'},
           sleep_base.replace('[[SLEEP_TEXT]]', stext), 'sleep_cross_sell')
    cart_add(f'v1_sleep_{tier}_add', f'Add {ssku}', 8, ssku, 'v1_sec_closing')

# ─ Closing: read-back, SMS consent, authorize, submit ─
section('v1_sec_closing', 'Closing', 9, 'v1_closing')
select('v1_closing', 'Closing Read', 9, ['Continue', 'Change Call Type'],
       {'Continue': 'v1_sms', 'Change Call Type': 'v1_call_type'},
       script('cboClosing', {
           0: dyn('flow.first_name'), 1: dyn('flow.first_name'), 2: dyn('cart.items_summary'),
           3: dyn('flow.billing_address.firstName'), 4: dyn('flow.billing_address.lastName'),
           10: dyn('flow.billing_phone'), 11: '$' + dyn('cart.first_payment'),
           12: dyn('flow.autoship_confirm'), 13: dyn('flow.autoship_note'),
           14: 'captured securely by keypad \u2014 not visible to you; if the caller\u2019s digits don\u2019t sound right, use Change Payment Info after authorization',
       }), 'closing_read')
select('v1_sms', 'SMS Consent', 9, ['Yes', 'No'], {'Yes': 'v1_sms_yes', 'No': 'v1_sms_no'},
       script('cboSMSConsent'), 'sms_consent')
sv('v1_sms_yes', 'SMS consent: yes', 9, 'v1_sms_cf', sms_consent_marketing='true', sms_consent_transactional='true',
   sms_consent_at='{{now.iso}}')
sv('v1_sms_no', 'SMS consent: no', 10, 'v1_sms_cf', sms_consent_marketing='false', sms_consent_transactional='false',
   sms_consent_at='{{now.iso}}')
custom('v1_sms_cf', 'SMS Consent', 9, CF_SMS, 'sms_consent', '{{flow.sms_consent}} at {{flow.sms_consent_at}}', 'v1_confirm_submit')
select('v1_confirm_submit', 'Submit order?', 9, ['Yes - Submit Order', 'No - Go Back'],
       {'Yes - Submit Order': 'v1_auth', 'No - Go Back': 'v1_sms'},
       agent('AGENT: The order is about to be authorized and submitted. Are you sure the responses are correct?'), 'confirm_submit')
add('v1_auth', 'authorize_payment', 'Authorize Payment', 9,
    {'approved': 'v1_auth_ok', 'declined': 'v1_auth_declined', 'error': 'v1_auth_error'},
    provider='authorize_net', amountMode='cart_total', cardNumberField='pan', expField='expiry', cvvField='cvv',
    zipField='{{flow.billing_address.zip}}', outputVariable='auth_result')
sv('v1_auth_ok', 'Count attempt', 9, 'v1_order', auth_attempts='{{flow.auth_attempts + 1}}')
add('v1_order', 'api_call', 'Submit Life Seasons Order', 9,
    {'success': 'v1_order_ok', 'error': 'v1_ordfail', 'timeout': 'v1_ordfail'},
    apiEndpointId=ORDER_ENDPOINT, apiDefinitionScope='tenant', apiDefinitionName='Life Seasons Order API',
    apiEndpointName='Add Order', outputVariable='order_response', timeoutSeconds=30, oncePerCall=True)
sv('v1_order_ok', 'Disposition: Order', 9, 'v1_disp_close_order', disposition='Order')
sv('v1_auth_declined', 'Count attempt', 10, 'v1_auth_exhausted', auth_attempts='{{flow.auth_attempts + 1}}')
branch('v1_auth_exhausted', '3 attempts used?', 10, '{{flow.auth_attempts}} >= 3', 'v1_decline_final', 'v1_decline')
DECLINE_INFO = ('<p>Agent: The authorization failed due to the below reason.  Select the appropriate option. '
                'Up to 3 attempts can be made to authorize the transaction.</p><p>Transaction Failure Reason:</p>'
                f'<p>{dyn("flow.auth_result.responseReasonText")}</p><p>Customer Info (for reference):</p>'
                f'<p>{dyn("flow.billing_address.firstName")} {dyn("flow.billing_address.lastName")}<br>'
                f'{dyn("flow.billing_address.address1")} {dyn("flow.billing_address.address2")}<br>'
                f'{dyn("flow.billing_address.city")}, {dyn("flow.billing_address.state")} {dyn("flow.billing_address.zip")}</p>')
select('v1_decline', 'Decline', 10, ['Change Customer Info', 'Change Payment Info', 'Cancel Order'],
       {'Change Customer Info': 'v1_sec_customer', 'Change Payment Info': 'v1_sec_payment', 'Cancel Order': 'v1_declined_disp'},
       DECLINE_INFO, 'decline_action')
select('v1_decline_final', 'Decline — attempts exhausted', 10, ['Cancel Order'], {'Cancel Order': 'v1_declined_disp'},
       f'<p>3 attempts have been made to authorize the transaction without success.  The order MUST be canceled.</p>'
       f'<p>{dyn("flow.auth_result.responseReasonText")}</p>', 'decline_action')
add('v1_auth_error', 'script', 'Authorization error', 10, {'default': 'v1_decline'},
    content=agent('AGENT: There was an error processing the transaction \u2014 THIS IS NOT A DECLINE.')
            + f'<p>{dyn("flow.auth_result.responseReasonText")}</p>')
sv('v1_declined_disp', 'Cancel → All Other Calls / Credit Card Declined', 10, 'v1_disp_close_aoc',
   call_type='All Other Calls', disposition='Credit Card Declined')
ORDFAIL = ['Change Customer Info', 'Change Payment Info', 'Order Post keeps failing (IT will be notified)']
select('v1_ordfail', 'Order Post Failure', 10, ORDFAIL,
       {ORDFAIL[0]: 'v1_sec_customer', ORDFAIL[1]: 'v1_sec_payment', ORDFAIL[2]: 'v1_ordfail_email'},
       script('cboOrdFail', {0: dyn('flow.order_response.error')}), 'order_post_failure')
add('v1_ordfail_email', 'send_email', 'Email IT — order post failure', 10, {'default': 'v1_ordfail_disp'},
    emailTo='', emailCc='', emailBcc='', emailFromName='Life Seasons Order Post Failure', emailReplyTo='',
    emailSubject='Life Seasons - Order Post Failure ({{call_record.order_number}})',
    emailBodyHtml='<p>Life Seasons: Failure Posting Order for call {{call_record.id}} (order {{call_record.order_number}}).</p>'
                  '<p>Error: {{flow.order_response.error}}</p>'
                  '<p>Once the record has been corrected, re-submit the order and make sure it is included in the '
                  'Daily Call Detail and Response Data File exports.</p>')
sv('v1_ordfail_disp', 'Disposition: Order (post failed)', 10, 'v1_disp_close_order', disposition='Order')

# ─ Dispositions + wrap-up ─
AOC = ['Alzheimers Consumer', 'Credit Card Declined', 'Disconnected During Offer', 'Language Barrier/International',
       'No MOP (No Method of Payment)', 'Not Interested (Disconnected after offer presentation)', 'Price Objection Too Expensive',
       'Referred to Customer Service', 'Shipping to Canada', 'Spousal Objection', 'Thought it was free',
       'Too many upsells - Caller Fatigue', 'Transferred to Customer Service', 'Wants to speak to Doctor']
JUNK = ['Accidental Screen Pop', 'Busy Signal', 'Call Recording', 'Ghost Call/Dead Air', 'Prank Caller', 'Test Call',
        'Voicemail', 'Wrong Number']
EXPLAIN = {'Referred to Customer Service'}
AOC_WRAP, JUNK_WRAP = wrapup('aoc', 11), wrapup('junk', 12)
select('v1_disp_aoc', 'Disposition — All Other Calls', 11, AOC,
       {d: ('v1_explain' if d in EXPLAIN else AOC_WRAP) for d in AOC},
       '<p>"Thank you for calling and have a great day!"</p>' + agent('AGENT: Select the appropriate disposition'), 'disposition')
select('v1_disp_junk', 'Disposition — Junk', 11, JUNK, {d: JUNK_WRAP for d in JUNK},
       agent('AGENT: Select the appropriate disposition'), 'disposition')
text_input('v1_explain', 'Explain', 11, 'v1_aoc_cf1', agent('AGENT: An explanation is required for this disposition.'),
           'disposition_reason', required=True)
add('v1_disp_close_aoc', 'script', 'Closing — All Other Calls', 11, {'default': wrapup('close', 11)},
    content='<p>"Thank you for calling and have a great day!"</p>')
add('v1_disp_close_canada', 'script', 'Closing - Canada', 11, {'default': wrapup('canada', 11)},
    content='<p>"Thank you for your interest in NeuroQ. At this time we cannot ship to Canada."</p>')
add('v1_disp_close_order', 'script', 'Closing — Order', 11, {'default': wrapup('order', 11)},
    content='<p>"I want to thank you for calling and I hope you have a great day!"</p>')

# ── Validate graph ────────────────────────────────────────────────────────────
for nid, n in nodes.items():
    for t, target in n['transitions'].items():
        assert target in nodes, (nid, t, target)
    if n['type'] == 'input' and n.get('fieldType') == 'select':
        assert set(n['transitions']) == {o['value'] for o in n['options']}, nid
reach, stack = set(), ['v1_start']
while stack:
    x = stack.pop()
    if x in reach: continue
    reach.add(x); stack.extend(nodes[x]['transitions'].values())
unreached = set(nodes) - reach
assert not unreached, unreached
leftover = [nid for nid, n in nodes.items() if '[[CODE' in json.dumps(n)]
assert not leftover, leftover

definition = {'name': 'NeuroQ - V1 (from CRMPro, draft)', 'flow_type': 'crm', 'entry_node': 'v1_start', 'nodes': nodes}
from layout import layout
print('layout', layout(definition))
open(os.path.join(HERE, 'v1_flow.json'), 'w', encoding='utf-8').write(json.dumps(definition, ensure_ascii=False, indent=2))

with open(os.path.join(HERE, 'v1_review.txt'), 'w', encoding='utf-8') as f:
    for nid, n in nodes.items():
        txt = n.get('scriptContent') or n.get('content') or ''
        txt = re.sub(r'<[^>]+>', ' ', txt)
        f.write(f"[{n['type']}] {nid} — {n['label']}\n   {re.sub(r' +', ' ', txt).strip()[:700]}\n")
        if n.get('options'): f.write(f"   options: {[o['value'] for o in n['options']]}\n")


# ── SQL seed ─────────────────────────────────────────────────────────────────
def q(s): return 'NULL' if s is None else "'" + str(s).replace("'", "''") + "'"


sql = [f"set search_path=tenant_test_tenant,public;", "begin;",
       f"create temp table t as select id as tenant_id from public.tenants where subdomain={q(TENANT_SUB)};"]
# Products keyed by SKU (not a generated id) so re-running the seed never duplicates a family.
for fam, fam_desc in FAMILIES.items():
    sql.append(f"""insert into products (id, tenant_id, sku, description, searchable, reporting_only, weight, canada_surcharge,
  akhi_surcharge, outlying_us_surcharge, foreign_surcharge, inventory_status, decrement_on_order, qty_available, minimum_qty,
  qty_limit, qty_limit_exception, expected_quantity, alias_skus, keywords, created_at, updated_at, qty_reserved, client_id)
select gen_random_uuid(), tenant_id, {q(fam)}, {q(fam_desc)}, true, false, 0, 0, 0, 0, 0, 'Available', false, 0, 0, 0, 0, 0,
  '[]', '[]', now(), now(), 0, {q(CLIENT)} from t on conflict (sku) do nothing;""")
for sku, desc, price, sh, days, cannella in CATALOG:
    payments = json.dumps([{'paymentNumber': 1, 'description': 'Full payment', 'amount': price, 'intervalDays': 0, 'paymentId': None}])
    intervals = json.dumps([{'intervalDays': days, 'autoShipId': None}] if days else [])
    flags = json.dumps([{'name': 'Cannella SKU', 'value': cannella}] if cannella else [])
    sql.append(f"""insert into offers (id, tenant_id, product_id, name, full_price, allow_price_override, shipping, tax_exempt,
  shipping_exempt, is_upsell, upsell_qty, upsell_qty_of_entry, upsell_commission, upsell_client_amount, auto_ship,
  auto_ship_optional, allow_ship_to, ship_to_required, allow_delivery_message, ship_method_per_item, is_active, payments,
  quantity_price_breaks, mix_match_price_breaks, auto_ship_intervals, ship_methods, personalization, flags, created_at,
  updated_at, campaign_ids, client_id, sku)
select {q(OFFER[sku])}, tenant_id, (select id from products where sku = {q(sku[:3])}), {q(desc)}, {price}, false, {sh}, false, {str(sh == 0).lower()},
  {str(sku[:3] in ('303', '307')).lower()}, 0, 0, 0, 0, {str(days > 0).lower()}, false, false, false, false, false, true,
  {q(payments)}, '[]', '[]', {q(intervals)}, '[]', '[]', {q(flags)}, now(), now(), ARRAY[{q(CAMPAIGN)}]::uuid[], {q(CLIENT)}, {q(sku)}
from t on conflict (id) do nothing;""")
for defid, field, label in [(CF_REASON, 'disposition_reason', 'Disposition Reason'), (CF_SMS, 'sms_consent', 'SMS Consent')]:
    sql.append(f"""insert into custom_field_definitions (id, tenant_id, client_id, campaign_id, field_name, display_label,
  data_type_name, is_required, display_order, is_active)
select {q(defid)}, tenant_id, {q(CLIENT)}, {q(CAMPAIGN)}, {q(field)}, {q(label)}, 'string', false, 0, true
from t on conflict (id) do nothing;""")
sql.append(f"""delete from flows where id = {q(FLOW_ID)};
insert into flows (id, tenant_id, client_id, campaign_id, name, flow_type, version, is_active, definition, created_at,
  updated_at, created_by_agent_id)
select {q(FLOW_ID)}, tenant_id, {q(CLIENT)}, {q(CAMPAIGN)}, {q(definition['name'])}, 'crm', 1, false,
  {q(json.dumps(definition, ensure_ascii=False))}::jsonb, now(), now(), {q(CREATED_BY)} from t;""")
sql.append("commit;")
open(os.path.join(HERE, 'v1_seed.sql'), 'w', encoding='utf-8').write('\n'.join(sql) + '\n')
print(f'{len(nodes)} nodes, {len(CATALOG)} offers, flow {FLOW_ID}')
