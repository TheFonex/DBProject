# BD2_Project

Informacje dotyczące realizacji projektu zaliczeniowego:

- projekt składa się z dwóch części:
    - działający kod znajdujący się w repozytorium
    - raport (w stylu MLA) opisujący projekt (raport powinien przedstawiać problematykę projektu, użytą technologię, zwięzłe przedstawienie działania aplikacji, rezultaty działania aplikacji, referencje do źródeł zewnętrznych)

- terminem oddania projektu jest data przedostatnich zajęć w ramach laboratorium (liczy się data commitu kodu i raportu do repozytorium)

## Opis projektu

### Team
- Kacper Cieslak
- Daniel Kuzmierkiewicz
- Michal Petrykowski

### Temat
Wizualizacja komunikacji pomiedzy node'ami serwerow bazy danych tworzacych klaster.

### Opis
Wykonanie aplikacji, ktora bedzie wizualizowac dzialanie bazy danych, ktora w oddzielnych obrazach dockera jest polaczona jako jeden klaster. Aplikacja zostanie stworzona uzywajac jednej z bibliotek c# (jest ich wiele ale dzialaja podobnie, jeszcze do decyzji). Kazdy node klastra bedzie pokazany na planszy aplikacji oraz wyswietlona bedzie komunikacja pomiedzy oddzielnymi node'ami klastra.

### Raport w stylu MLA jest w repo

# How to run this project
## Establish the mongodb cluster
### Docker Compose
- Go into the X Node Cluster directory, where X = 3, 5
- Open the folder in the terminal
```
> docker-compose up -d
```
## Open the application

### Open the vs solutiuon file in visual studio and compile and run the application

### Select correct docker-compose file
The application will ask to provide correct docker-compose file from which the cluster was created, it is to simplify the creation of the connection string to the database.

### Rest of the functionality is covered in the raport.