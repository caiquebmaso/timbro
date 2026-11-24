@echo off
rem Tira o motor do Timbro de todos os aparelhos e devolve o som como era.
rem Use se o som sumir ou ficar estranho. Pede administrador.
echo Tirando o motor do Timbro de todos os aparelhos...
powershell -NoProfile -Command "Start-Process -FilePath '%LOCALAPPDATA%\Timbro\timbro.exe' -ArgumentList '--remover-tudo' -Verb RunAs -Wait"
echo Pronto. Se o som nao voltar, reinicie o PC.
pause
