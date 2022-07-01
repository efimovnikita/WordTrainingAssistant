# WordTrainingAssistant
SkyEng vocabulary training application.

### Arguments at the first start of an application
```shell
./WordTrainingAssistant -d "chrome_driver_folder" -l "skyeng_login" -p "skyeng_password" -e "dictionary_file"
```
### Arguments for subsequent launches of the application
```shell
./WordTrainingAssistant
```
### Full arguments list
```
Usage:
  WordTrainingAssistant [options]

Options:
  --dir <dir>                                    Path to SkyEng dictionary pages folder
  -c, --count <count>                            Number of words to be trained [default: 20]
  -o, --offline                                  Offline mode [default: False]
  --useCache                                     Use words cache [default: True]
  -e, --externalDictionary <externalDictionary>  The path to the external dictionary file
  -d, --driver <driver>                          Google Chrome browser driver
  -l, --login <login>                            Login for an SkyEng account
  -p, --password <password>                      Password for an SkyEng account
  --version                                      Show version information
  -?, -h, --help                                 Show help and usage information
```
### Google browser drivers storage
https://chromedriver.storage.googleapis.com/index.html
