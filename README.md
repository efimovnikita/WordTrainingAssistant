# WordTrainingAssistant
SkyEng vocabulary training application.

### Arguments for start of an application
```shell
./words -l "skyeng_login" -p "skyeng_password" -s "skyeng_student_id" -e "dictionary_file" -d "chrome_driver_folder"
```
### Full arguments list
```
Usage:
  words [options]

Options:
  -c, --count <count>                   Number of words to be trained [default: 20]
  -o, --offline                         Offline mode [default: False]
  --useCache                            Use words cache [default: True]
  -e, --dictionary <dictionary>         The path to the external dictionary file
  -d, --driver <driver> (REQUIRED)      Google Chrome browser driver
  -l, --login <login> (REQUIRED)        Login for an SkyEng account
  -p, --password <password> (REQUIRED)  Password for an SkyEng account
  -s, --student <student> (REQUIRED)    Student id
  --version                             Show version information
  -?, -h, --help                        Show help and usage information
```
### Google browser drivers storage
https://chromedriver.storage.googleapis.com/index.html
