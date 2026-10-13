"""Mappings to the manufacturer's solid CAD files in the public source archives.

The library builder records the resolved URLs and SHA-256 hashes; runtime export
never searches the web or guesses a part from its display name.
"""
def mappings(files):
    result = {}
    def add(key, token, require='', exclude=''):
        candidates = [p for p in files if token.lower() in p.name.lower()
                      and (not require or require.lower() in str(p).lower())
                      and (not exclude or exclude.lower() not in str(p).lower())]
        candidates.sort(key=lambda p: (not 'TlorschCode' in str(p), len(str(p)), str(p)))
        if not candidates:
            print('SOURCE MISSING',key,token, flush=True)
        else:
            result[key] = candidates[0]
    for key,sku in {
        'BTRY':'276-4811','BTCL':'276-6020','BRAN':'V5_Robot_Brain_276-4810.STEP',
        'CHAIN-Pitch9p79':'276-2252-001','CHAIN-Pitch3p75':'CHAIN-LINK.STEP','CHAIN-Pitch6p35':'CHAIN-LINK.STEP',
        'INSERT-Metal':'276-3881-002','INSERT-Plastic':'276-3881-001',
        'SHFT-Normal-1':'Drive_Shaft_12_276-1149','SHFT-High Strength-1':'276-3524',
        'PLTE-5-5':'275-1140',
        'LMTK-Inner Slide Trunk':'276-1926-003','LMTK-Outer Slide Trunk':'276-1926-004',
        'LMTK-Linear Slide Bracket':'276-5771','RAIL-Rail-24':'276-1926-001',
        'MCMS-Worm and Wheel-Gear':'Worm Gear & Wheel','MCMS-Worm and Wheel-Worm':'Worm Gear & Wheel',
        'MOTR-11W':'276-4840','MOTR-5.5W':'276-4842','RDIO':'276-4831',
        'SNSR-Potentiometer-V5':'276-7417','SNSR-Distance-V5':'276-4852',
        'SNSR-Distance-Cortex':'276-2155','SNSR-Rotation-V5':'276-6050',
        'SNSR-Rotation-Cortex':'276-2156','SNSR-Optical-V5':'276-7043',
        'SNSR-Optical-Cortex':'276-2158','SNSR-Inertial-V5':'276-4855',
        'SNSR-Vision-V5':'276-4850','SNSR-Line Tracker-Cortex':'276-2154',
        'SWCH-Bumper-V5':'276-4858','SWCH-Bumper-Cortex':'276-2159','SWCH-Limit-Cortex':'276-2174',
        'TANK-V5':'Pneumatic Reservoir Assembly','TANK-Legacy':'Reservoir.STEP',
        'BLCK-Normal':'276-2016-001','BLCK-High Strength':'276-8383',
        'BRNG-Normal':'276-1209','BRNG-High Strength':'276-3521','BRNG-Low Profile':'276-8023',
        'BRNG-Collar Retainer':'276-8024','HexNR-Nut Retainer':'276-6482',
        'HexNR-Bearing Retainer':'276-6481','HexNR-Square Retainer':'276-6483',
        'LKBR-Metal':'275-1065','LKBR-Plastic':'276-2016-002','RBMP':'276-7499',
        'CLMP-Normal-Normal':'276-2010','CLMP-Clamping-Normal':'276-3891',
        'CLMP-Clamping-High Strength':'276-3520','CLMP-Clamping-Low Profile HS':'276-7580',
        'WSHR-Steel':'275-1024','WSHR-Teflon':'275-1025',
        'ANGL-1x1-35':'217-6484','ANGL-2x2-35':'275-1143','ANGL-3x3-35':'275-1144',
        'CCHL-1x2-35':'276-2289','CCHL-1x3-35':'276-4359','CCHL-1x5-35':'276-2298',
        'UCHL-2x2-35':'276-7285','BSPT':'276-1341','RAIL-2x25':'275-1145','RAIL-2x35':'275-1146',
        'NUT-Lock':'275-1027','NUT-Keps':'275-1026','NUT-Hex':'275-1028','NUT-Low Profile':'276-7767',
        'GSET-Coupler-Angle':'276-2578','GSET-Coupler-Angle Corner':'276-2576','GSET-Coupler-Channel':'276-2575',
        'GSET-45 Degree-Plate':'275-1186','GSET-90 Degree-Angle':'90-Degree Gusset Set - Angle',
        'GSET-90 Degree-Plate':'90-Degree Gusset Set - Plate','GSET-Pack-Angle':'Gusset Pack - Angle',
        'GSET-Pack-Pivot':'Gusset Pack - Pivot','GSET-Pack-Plus':'Gusset Pack - Plus',
        'MCMS-Bevel Gear-16T':'276-2045-051','MCMS-Bevel Gear-32T':'276-2045-052',
        'MCMS-Drop Off Cam-':'276-2045-061','MCMS-Screw Kit-Bracket':'276-2045-033',
        'MCMS-Screw Kit-Worm Nut':'276-2045-031','MCMS-Screw Kit-Worm Gear':'276-2045-032',
        'MCMS-Cam Follower-':'276-2045-066','MCMS-Rack-':'276-4782','MCMS-Hand Crank-':'276-2045-001',
        'MECN':'276-1447',
        'OMNI-V1-2.75in':'276-1902','OMNI-V1-3.25in':'276-3526','OMNI-V1-4.00in':'276-2185',
        'OMNI-V2-2.00in':'276-9044','OMNI-V2-2.75in':'276-8106','OMNI-V2-3.25in':'276-8026','OMNI-V2-4.00in':'276-8107',
        'TWHL-V1-2.75in':'276-1496','TWHL-V1-3.25in':'276-3525','TWHL-V1-4.00in':'276-1497)',
        'TWHL-V1-5.00in':'276-1498','TWHL-V2-2.75in':'276-8098','TWHL-V2-3.25in':'276-7771','TWHL-V2-4.00in':'276-8103',
    }.items():
        add(key,sku)
    for degree,sku in [(30,7758),(45,7759),(60,7760),(90,7761)]:
        for kind,end in [('Flat','001'),('Bent','002')]:
            add(f'GSET-{degree} Degree-{kind}',f'276-{sku}-{end}')
    for kind,skus,teeth in [('Normal',[2169001,2169002,2169003,2169004],[12,36,60,84]),
                           ('High Strength',[2251,5034,5035,3438],[12,36,60,84]),
                           ('High Strength v2',[7572,7747,7573,7748,7574,7749],[24,36,48,60,72,84])]:
        for sku,t in zip(skus,teeth):
            token=f'276-{sku}' if sku<10000 else f'276-2169-{sku%1000:03d}'
            add(f'GEAR-{kind}-{t}T',token)
    for teeth in [10,15,24,40,48]:
        add(f'SPKT-Normal-{teeth}T',f'VEX-{teeth}-TOOTH')
    for teeth,sku in zip([6,12,18,24,30],range(3876,3881)):
        add(f'SPKT-High Strength-{teeth}T',f'276-{sku}',require='HS Bore')
    for kind,items in {
        'Nylon': [('1/8in','275-1066-001'),('1/4in','275-1066-002'),('3/8in','275-1066-003'),('1/2in','275-1066-004')],
        'High Strength':[('1/16in','276-3441-001'),('1/8in','276-3441-002'),('1/4in','276-3441-003'),('1/2in','276-3441-004')],
        'Plastic':[('4.6mm','276-2018'),('8mm','276-2019')]
    }.items():
        for size,sku in items: add(f'SPCR-{kind}-{size}',sku)
    for size,sku in zip(['1/4in','1/2in','3/4in','1.00in','1.50in','2.00in','2.50in','3.00in','4.00in','5.00in','6.00in'],range(1013,1024)):
        add('SNDF-'+size,f'275-{sku}')
    for size,token in zip(['1/4in','3/8in','1/2in','5/8in','3/4in','7/8in','1.00in','1.25in','1.50in','1.75in','2.00in','2.25in','2.50in'],
                          ['0.25 Inch','0.375 Inch','0.50 Inch','0.625 Inch','0.75 in','0.875 Inch','1.00 Inch','1.25 Inch','1.50 Inch','1.75 Inch','2.00 Inch','2.25 Inch','2.50 Inch']):
        add('SCRW-'+size,token+' Star Drive Screw')
    for mm in [25,50,75]:
        for state in ['Normal','Extended']: add(f'PNMT-{mm}mm-{state}',f'{mm}mm Stroke Pneumatic Cylinder')
    for size,sku in [('1.625in',6350),('2.00in',6353),('3.00in',6447),('4.00in',6450)]:
        for style in ['With Adapters','No Adapters']: add(f'FWHL-{style}-{size}',f'217-{sku}')
    return result
