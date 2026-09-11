'This line must remain unchanged if this is changed or the iLogic Level of Detail is renamed or removed the rule will not work properly
ThisApplication.ActiveDocument.ComponentDefinition.RepresentationsManager.LevelofDetailRepresentations("iLogic").Activate


''  Coil Suppression
If CoilsHigh = 1
Component.IsActive("CoilBtm:1") = True
Component.IsActive("CoilMid:1") = False
Component.IsActive("CoilTop:1") = False
Else If CoilsHigh = 2
Component.IsActive("CoilBtm:1") = True
Component.IsActive("CoilMid:1") = True
Component.IsActive("CoilTop:1") = False
Else If CoilsHigh = 3
Component.IsActive("CoilBtm:1") = True
Component.IsActive("CoilMid:1") = True
Component.IsActive("CoilTop:1") = True
End If

''SUPRESSION OF US AND DS BOTTOM BULKHEAD
Component.IsActive("091-30102-458:1") = Part_091_30102_458 = 1
Component.IsActive("091-30102-484:1") = Part_091_30102_458 = 1

Component.IsActive("091-30102-459:1") = Part_091_30102_459 = 1
Component.IsActive("091-30102-460:1") = Part_091_30102_459 = 1
Component.IsActive("091-30102-485:1") = Part_091_30102_459 = 1
Component.IsActive("091-30102-486:1") = Part_091_30102_459 = 1
'-----------------------------------------------------------------------
'' SUPPRESSION OF US TOP BULKHEAD
Component.IsActive("091-30102-463:1") = Part_091_30102_463 = 1

Component.IsActive("091-30102-464:1") = Part_091_30102_464 = 1
Component.IsActive("091-30102-465:1") = Part_091_30102_464 = 1

'' SUPPRESSION OF STACKING ANGLE
Component.IsActive("091-30102-466:1") = Part_091_30102_466_1 = 1

Component.IsActive("091-30102-466:2") = Part_091_30102_466_2 = 1

Component.IsActive("091-30102-467:1") = Part_091_30102_467_1 = 1
Component.IsActive("091-30102-468:1") = Part_091_30102_467_1 = 1

Component.IsActive("091-30102-467:2") = Part_091_30102_467_2 = 1
Component.IsActive("091-30102-468:2") = Part_091_30102_467_2 = 1

'' SUPPRESSION OF STACKING RACK DRIP PAN A
Component.IsActive("091-30102-469:1") = Part_091_30102_469_1 = 1

Component.IsActive("091-30102-469:2") = Part_091_30102_469_2 = 1

Component.IsActive("091-30102-470:1") = Part_091_30102_470_1 = 1
Component.IsActive("091-30102-471:1") = Part_091_30102_470_1 = 1

Component.IsActive("091-30102-470:2") = Part_091_30102_470_2 = 1
Component.IsActive("091-30102-471:2") = Part_091_30102_470_2 = 1

'' SUPPRESSION OF HEATING COIL SHELF FOR STACKED AND ELEVATED HEATING COILS
Component.IsActive("091-30102-472:1") = Part_091_30102_472 = 1

Component.IsActive("091-30102-473:1") = Part_091_30102_473 = 1

Component.IsActive("091-30102-474:1") = Part_091_30102_473 = 1
'-----------------------------------------------------------------------
Component.IsActive("091-30102-475:1") = Part_091_30102_475_1 = 1

Component.IsActive("091-30102-475:2") = Part_091_30102_475_2 = 1

Component.IsActive("091-30102-475:3") = Part_091_30102_475_3 = 1
'-----------------------------------------------------------------------
Component.IsActive("091-30102-476:1") = Part_091_30102_476_1 = 1
Component.IsActive("091-30102-477:1") = Part_091_30102_476_1 = 1

Component.IsActive("091-30102-476:2") = Part_091_30102_476_2 = 1
Component.IsActive("091-30102-477:2") = Part_091_30102_476_2 = 1

Component.IsActive("091-30102-476:3") = Part_091_30102_476_3 = 1
Component.IsActive("091-30102-477:3") = Part_091_30102_476_3 = 1

'' SUPPRESSION OF RIGHT AND LEFT  STACKING RACK POSTS, FLOOR ANGLE CONDENSATE
Component.IsActive("091-30102-478:1") = Part_091_30102_478 = 1
Component.IsActive("091-30102-479:1") = Part_091_30102_478 = 1

Component.IsActive("091-30102-480:1") = Part_091_30102_480 = 1
Component.IsActive("091-30102-480:2") = Part_091_30102_480 = 1

'' SUPPRESSION OF STACKING RACK DRIP PAN END
Component.IsActive("091-30102-481:1") = Part_091_30102_481_1 = 1
Component.IsActive("091-30102-481:2") = Part_091_30102_481_1 = 1

Component.IsActive("091-30102-481:3") = Part_091_30102_481_2 = 1
Component.IsActive("091-30102-481:4") = Part_091_30102_481_2 = 1

'' SUPPRESSION OF POST TIE ANGLE
Component.IsActive("091-30102-482:1") = Part_091_30102_482 = 1
Component.IsActive("091-30102-482:2") = Part_091_30102_482 = 1

Component.IsActive("091-30102-483:1") = Part_091_30102_483 = 1
Component.IsActive("091-30102-483:2") = Part_091_30102_483 = 1

'' SUPPRESSION OF DS TOP BULKHEADS
Component.IsActive("091-30102-487:1") = Part_091_30102_487 = 1

Component.IsActive("091-30102-488:1") = Part_091_30102_488 = 1

Component.IsActive("091-30102-489:1") = Part_091_30102_489 = 1
Component.IsActive("091-30102-490:1") = Part_091_30102_489 = 1

Component.IsActive("091-30102-491:1") = Part_091_30102_491 = 1
Component.IsActive("091-30102-492:1") = Part_091_30102_491 = 1

'' SUPPRESSION OF DS STACKING RACK SUPPORTS
Component.IsActive("091-30102-493:1") = Part_091_30102_493_1 = 1
Component.IsActive("091-30102-494:1") = Part_091_30102_493_1 = 1

Component.IsActive("091-30102-493:2") = Part_091_30102_493_2 = 1
Component.IsActive("091-30102-494:2") = Part_091_30102_493_2 = 1

Component.IsActive("091-30102-493:3") = Part_091_30102_493_3 = 1
Component.IsActive("091-30102-494:3") = Part_091_30102_493_3 = 1

Component.IsActive("091-30102-493:4") = Part_091_30102_493_4 = 1
Component.IsActive("091-30102-494:4") = Part_091_30102_493_4 = 1

Component.IsActive("091-30102-493:5") = Part_091_30102_493_5 = 1
Component.IsActive("091-30102-494:5") = Part_091_30102_493_5 = 1

Component.IsActive("091-30102-493:6") = Part_091_30102_493_6 = 1
Component.IsActive("091-30102-494:6") = Part_091_30102_493_6 = 1

Component.IsActive("091-30102-493:7") = Part_091_30102_493_7 = 1
Component.IsActive("091-30102-494:7") = Part_091_30102_493_7 = 1

Component.IsActive("091-30102-493:8") = Part_091_30102_493_8 = 1
Component.IsActive("091-30102-494:8") = Part_091_30102_493_8 = 1

Component.IsActive("091-30102-493:9") = Part_091_30102_493_9 = 1
Component.IsActive("091-30102-494:9") = Part_091_30102_493_9 = 1

Component.IsActive("091-30102-493:10") = Part_091_30102_493_10 = 1
Component.IsActive("091-30102-494:10") = Part_091_30102_493_10 = 1

Component.IsActive("091-30102-495:1") = Part_091_30102_495 = 1

Component.IsActive("091-30102-512:1") = Part_091_30102_512 = 1
Component.IsActive("091-30102-513:1") = Part_091_30102_512 = 1

'' SUPPRESSION OF DS STACKING RACK DRIP PAN SUPPORTS
Component.IsActive("091-30102-496:1") = Part_091_30102_496_1 = 1
Component.IsActive("091-30102-517:1") = Part_091_30102_496_2 = 1
Component.IsActive("091-30102-496:3") = Part_091_30102_496_3 = 1
Component.IsActive("091-30102-496:4") = Part_091_30102_496_4 = 1
Component.IsActive("091-30102-496:5") = Part_091_30102_496_5 = 1
Component.IsActive("091-30102-496:6") = Part_091_30102_496_6 = 1
Component.IsActive("091-30102-496:7") = Part_091_30102_496_7 = 1
Component.IsActive("091-30102-496:8") = Part_091_30102_496_8 = 1
Component.IsActive("091-30102-518:1") = Part_091_30102_496_9 = 1
Component.IsActive("091-30102-496:10") = Part_091_30102_496_10 = 1
Component.IsActive("091-30102-496:11") = Part_091_30102_496_11 = 1
Component.IsActive("091-30102-517:2") = Part_091_30102_496_12 = 1
Component.IsActive("091-30102-496:13") = Part_091_30102_496_13 = 1
Component.IsActive("091-30102-496:14") = Part_091_30102_496_14 = 1
Component.IsActive("091-30102-496:15") = Part_091_30102_496_15 = 1
Component.IsActive("091-30102-496:16") = Part_091_30102_496_16 = 1
Component.IsActive("091-30102-496:17") = Part_091_30102_496_17 = 1
Component.IsActive("091-30102-496:18") = Part_091_30102_496_18 = 1
Component.IsActive("091-30102-518:2") = Part_091_30102_496_19 = 1
Component.IsActive("091-30102-496:20") = Part_091_30102_496_20 = 1

'' SUPPRESSION OF DS TOP COIL NON HEADER SIDE BULKHEAD
Component.IsActive("091-30102-497:1") = Part_091_30102_497 = 1

Component.IsActive("091-30102-498:1") = Part_091_30102_498 = 1

'' SUPPRESSION OF TOP COIL HEADER SIDE SUPPORT
Component.IsActive("091-30102-499:1") = Part_091_30102_499_1 = 1
Component.IsActive("091-30102-499:2") = Part_091_30102_499_2 = 1
Component.IsActive("091-30102-499:3") = Part_091_30102_499_3 = 1
Component.IsActive("091-30102-499:4") = Part_091_30102_499_4 = 1
Component.IsActive("091-30102-499:5") = Part_091_30102_499_5 = 1
Component.IsActive("091-30102-499:6") = Part_091_30102_499_6 = 1

'' SUPPRESSION OF US STACKING RACK SUPPORTS
Component.IsActive("091-30102-500:1") = Part_091_30102_500_1 = 1
Component.IsActive("091-30102-503:1") = Part_091_30102_500_1 = 1
Component.IsActive("091-30102-505:1") = Part_091_30102_500_1 = 1

Component.IsActive("091-30102-500:2") = Part_091_30102_500_2 = 1
Component.IsActive("091-30102-503:2") = Part_091_30102_500_2 = 1
Component.IsActive("091-30102-505:2") = Part_091_30102_500_2 = 1

Component.IsActive("091-30102-500:3") = Part_091_30102_500_3 = 1
Component.IsActive("091-30102-503:3") = Part_091_30102_500_3 = 1
Component.IsActive("091-30102-505:3") = Part_091_30102_500_3 = 1

Component.IsActive("091-30102-501:1") = Part_091_30102_501_1 = 1
Component.IsActive("091-30102-502:1") = Part_091_30102_501_1 = 1
Component.IsActive("091-30102-504:1") = Part_091_30102_501_1 = 1

Component.IsActive("091-30102-501:2") = Part_091_30102_501_2 = 1
Component.IsActive("091-30102-502:2") = Part_091_30102_501_2 = 1
Component.IsActive("091-30102-504:2") = Part_091_30102_501_2 = 1

Component.IsActive("091-30102-501:3") = Part_091_30102_501_3 = 1
Component.IsActive("091-30102-502:3") = Part_091_30102_501_3 = 1
Component.IsActive("091-30102-504:3") = Part_091_30102_501_3 = 1
'-----------------------------------------------------------------------
Component.IsActive("091-30102-504:4") = Part_091_30102_504_4 = 1
Component.IsActive("091-30102-504:5") = Part_091_30102_504_4 = 1

Component.IsActive("091-30102-504:8") = Part_091_30102_504_5 = 1
Component.IsActive("091-30102-504:9") = Part_091_30102_504_5 = 1

Component.IsActive("091-30102-504:12") = Part_091_30102_504_6 = 1
Component.IsActive("091-30102-504:13") = Part_091_30102_504_6 = 1

Component.IsActive("091-30102-504:6") = Part_091_30102_504_7 = 1
Component.IsActive("091-30102-504:7") = Part_091_30102_504_7 = 1

Component.IsActive("091-30102-504:10") = Part_091_30102_504_8 = 1
Component.IsActive("091-30102-504:11") = Part_091_30102_504_8 = 1

Component.IsActive("091-30102-504:14") = Part_091_30102_504_9 = 1
Component.IsActive("091-30102-504:15") = Part_091_30102_504_9 = 1
'-----------------------------------------------------------------------
Component.IsActive("091-30102-505:4") = Part_091_30102_505_4 = 1
Component.IsActive("091-30102-505:5") = Part_091_30102_505_4 = 1

Component.IsActive("091-30102-505:8") = Part_091_30102_505_5 = 1
Component.IsActive("091-30102-505:9") = Part_091_30102_505_5 = 1

Component.IsActive("091-30102-505:12") = Part_091_30102_505_6 = 1
Component.IsActive("091-30102-505:13") = Part_091_30102_505_6 = 1

Component.IsActive("091-30102-505:6") = Part_091_30102_505_7 = 1
Component.IsActive("091-30102-505:7") = Part_091_30102_505_7 = 1

Component.IsActive("091-30102-505:10") = Part_091_30102_505_8 = 1
Component.IsActive("091-30102-505:11") = Part_091_30102_505_8 = 1

Component.IsActive("091-30102-505:14") = Part_091_30102_505_9 = 1
Component.IsActive("091-30102-505:15") = Part_091_30102_505_9 = 1

'' SUPPRESSION OF DS HC BULKHEADS
Component.IsActive("091-30102-506:1") = Part_091_30102_506 = 1
Component.IsActive("091-30102-507:1") = Part_091_30102_507 = 1
Component.IsActive("091-30102-508:1") = Part_091_30102_508 = 1
Component.IsActive("091-30102-509:1") = Part_091_30102_509 = 1
Component.IsActive("091-30102-510:1") = Part_091_30102_510 = 1
Component.IsActive("091-30102-511:1") = Part_091_30102_511 = 1
Component.IsActive("091-30102-514:1") = Part_091_30102_514 = 1
Component.IsActive("091-30102-515:1") = Part_091_30102_515 = 1
Component.IsActive("091-30102-516:1") = Part_091_30102_516 = 1

'' End of Logic ''