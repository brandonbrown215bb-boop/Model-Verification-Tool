'set a reference to the document
'Return view to Home view
ThisApplication.CommandManager.ControlDefinitions.Item _
("AppViewCubeHomeCmd").Execute

'zoom all
ThisApplication.ActiveView.Fit
'-----end of ilogic-----