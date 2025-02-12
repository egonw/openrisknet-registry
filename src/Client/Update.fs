module Orn.Registry.Client.Update

open Elmish

open Orn.Registry.Shared
open Fable.Import

open Orn.Registry.Client.Types
open Orn.Registry.Client.Commands
// TODO: Try to make this query work



let ornServicesTestValues =
    [ { K8sService =
            { Name = "Test"
              ServicePorts = [| 8080 |] }
        OpenApiServiceInformation =
            { Description =
                  """Jaqpot v4 (Quattro) is the 4th version of a YAQP, a RESTful web platform which can be used to train machine learning models and use them to obtain toxicological predictions for given chemical compounds or engineered nano materials. Jaqpot v4 has integrated read-across, optimal experimental design, interlaboratory comparison, biokinetics and dose response modelling functionalities. The project is developed in Java8 and JEE7 by the <a href="http://www.chemeng.ntua.gr/labs/control_lab/"> Unit of Process Control and Informatics in the School of Chemical Engineering </a> at the <a href="https://www.ntua.gr/en/"> National Technical University of Athens.</a> """
              Endpoints = [ "/algorithm"; "/api/api.json"; "/algorithm/DecisionStump/bagging" ]
              OpenApiUrl = OpenApiUrl "http://someserivce/openapi.json"
              Name = "Jaqpot"
              RetrievedAt = System.DateTimeOffset.UtcNow } } ]

let testServices =
    Services
        { PlainK8sServices = []
          OrnServices = ornServicesTestValues
          ExternalOrnServices = []
          ExternalServices = []
          ExternalServiceLists = []
          Messages = [] }


let testSparqlResult =
    [| { ServiceName = "Test"
         OpenApiUrl = OpenApiUrl "http://bla.com/openapi"
         Result =
             BindingResult
                 { Variables = [ "subject"; "predicate"; "object" ]
                   ResultValues =
                       [ [ "Lazar REST Service^^http://www.w3.org/2001/XMLSchema#string"
                           "identity"
                           "Lazar REST Service^^http://www.w3.org/2001/XMLSchema#string" ] ] } } |]


let init (keycloak: IKeycloak): Model * Cmd<Msg> =


    let model = Authenticating

    model, Cmd.none


let update (msg: Msg) (model: Model): Model * Cmd<Msg> =
    let model', cmd =
        match msg with
        | AppMessage appmsg ->
            match model with
            | Authenticating
            | LoginError _ -> model, Cmd.none
            | LoggedIn appModel ->
                let newAppModel, cmd =
                    match appmsg with
                    | Refresh(Ok services) -> { appModel with Services = Services services }, sleep
                    | Refresh(Error err) -> { appModel with Services = ServicesError(err.ToString()) }, sleep
                    | SparqlQueryFinished(Ok results) -> { appModel with SparqlResults = Some results }, Cmd.none
                    | SparqlQueryFinished(Error err) ->
                        JS.console.log ("Error when running sparql query!", [ err ])
                        { appModel with SparqlResults = None }, Cmd.none
                    | RunSparqlQuery ->
                        { appModel with SparqlResults = None },
                        runSparqlQuery appModel.SelectedSparqlService appModel.SparqlQuery
                    | QueryChanged query ->
                        JS.console.log ("Query updated", [ query ])
                        { appModel with SparqlQuery = query }, Cmd.none
                    | Awake -> appModel, refresh appModel.LoginInfo.Token
                    | TabChanged newTab -> { appModel with ActiveTab = newTab }, Cmd.none
                    | ExternalServiceTextFieldChanged newText ->
                        { appModel with ExternalServiceTextFieldContent = newText }, Cmd.none
                    | AddExternalService ->
                        { appModel with ExternalServiceTextFieldContent = "" }
                        , addExternalService appModel.LoginInfo.Token appModel.ExternalServiceTextFieldContent
                    | RemoveExternalService service -> appModel, removeExternalService appModel.LoginInfo.Token service
                    | AddExternalServiceRequestCompleted(Ok _) -> appModel, Cmd.none
                    | AddExternalServiceRequestCompleted(Error err) ->
                        Fable.Import.JS.console.log ("Error:", [ err ])
                        appModel, Cmd.none
                    | RemoveExternalServiceRequestCompleted(Ok _) -> appModel, Cmd.none
                    | RemoveExternalServiceRequestCompleted(Error err) ->
                        Fable.Import.JS.console.log ("Error:", [ err ])
                        appModel, Cmd.none
                    | ExternalServiceListTextFieldChanged newText ->
                        { appModel with ExternalServiceListTextFieldContent = newText }, Cmd.none
                    | AddExternalServiceList ->
                        { appModel with ExternalServiceListTextFieldContent = "" }
                        , addExternalServiceList appModel.LoginInfo.Token appModel.ExternalServiceListTextFieldContent
                    | RemoveExternalServiceList list -> appModel, removeExternalServiceList appModel.LoginInfo.Token list
                    | AddExternalServiceListRequestCompleted(Ok _) -> appModel, Cmd.none
                    | AddExternalServiceListRequestCompleted(Error err) ->
                        Fable.Import.JS.console.log ("Error:", [ err ])
                        appModel, Cmd.none
                    | RemoveExternalServiceListRequestCompleted(Ok _) -> appModel, Cmd.none
                    | RemoveExternalServiceListRequestCompleted(Error err) ->
                        Fable.Import.JS.console.log ("Error:", [ err ])
                        appModel, Cmd.none
                    | SparqlSerivceSelected newSelection ->
                        { appModel with SelectedSparqlService = newSelection }, Cmd.none
                    | SparqlExampleQuerySelected newKey ->
                        { appModel with
                            SelectedExampleSparqlQuery = newKey
                            SparqlQuery = exampleQueries |> List.find (fun (key, value) -> key = newKey) |> snd
                            SparqlResults = None }, Cmd.none
                LoggedIn newAppModel, Cmd.map AppMessage cmd
        | KeycloakInit(Ok loginInfo) ->
            let localDebugMode = false

            let initialServices, initialCommand, initialResults =
                if localDebugMode then testServices, Cmd.none, Some testSparqlResult
                else ServicesLoading, refresh loginInfo.Token, None

            let newappModel =
                { Services = initialServices
                  InputSearchTerm =
                      { Text = ""
                        OntologyTerm = None
                        TermSuggestions = [] }
                  OutputSearchTerm =
                      { Text = ""
                        OntologyTerm = None
                        TermSuggestions = [] }
                  SparqlQuery = List.head exampleQueries |> snd
                  SparqlResults = initialResults
                  ActiveTab = ServicesTab
                  ExternalServiceTextFieldContent = ""
                  ExternalServiceListTextFieldContent = ""
                  SelectedSparqlService = ""
                  LoginInfo = loginInfo
                  SelectedExampleSparqlQuery = exampleQueries |> List.head |> fst }

            LoggedIn newappModel, Cmd.map AppMessage initialCommand
        | KeycloakInit(Error err) ->
            Fable.Import.JS.console.log ("Error: ", err)

            model, Cmd.none

    model', cmd
