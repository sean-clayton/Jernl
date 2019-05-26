namespace Jernl.Effects

module Actions =
    open DB
    open Jernl
    open Jernl.Hash
    open Jernl.Models
    open Newtonsoft.Json
    open Suave
    open Suave.RequestErrors
    open System
    open System.Text

    let jsonToString (json : 'a) = JsonConvert.SerializeObject(json)
    let tagQueryPart queryValue =
        sprintf """"article.tagList" : ["%s"]""" queryValue
    let authorQueryPart queryValue =
        sprintf """"article.author.username" : "%s" """ queryValue
    let favoriteQueryPart queryValue =
        sprintf """"article.author.username" : "%s", "article.favorited" : true"""
            queryValue

    let extractStringQueryVal (queryParameters : HttpRequest) name queryPart =
        match queryParameters.queryParam name with
        | Choice1Of2 queryVal -> " " + queryPart queryVal
        | Choice2Of2 _ -> String.Empty

    let extractNumericQueryVal (queryParameters : HttpRequest) name =
        match queryParameters.queryParam name with
        | Choice1Of2 limit -> Convert.ToInt32 limit
        | Choice2Of2 _ -> 0

    let hashPassword (request : UserRequest) =
        { request with
              user =
                  { request.user with hash = Crypto.fastHash request.user.password } }

    let registerNewUser dbClient =
        request (fun inputGraph ->
            JsonConvert.DeserializeObject<UserRequest>
                (inputGraph.rawForm |> ASCIIEncoding.UTF8.GetString)
            |> hashPassword
            |> registerWithBson dbClient
            |> Jernl.Convert.userRequestToUser
            |> jsonToString
            |> Successful.OK)

    let currentUserByEmail dbClient email =
        (getUser dbClient email).Value
        |> BsonDocConverter.toProfile
        |> jsonToString

    let withToken httpContext f =
        Auth.useToken httpContext (fun token ->
            async {
                try
                    let result = f token
                    return! Successful.OK (result) httpContext
                with ex ->
                    return! NOT_FOUND "Database error has occured" httpContext
            })

    let getCurrentUser dbClient httpContext =
        withToken httpContext
            (fun token -> currentUserByEmail dbClient token.UserName)

    let updateUser dbClient httpContext =
        withToken httpContext (fun token ->
            JsonConvert.DeserializeObject<UserRequest>
                (httpContext.request.rawForm |> ASCIIEncoding.UTF8.GetString)
            |> updateRequestedUser dbClient
            |> ignore
            String.Empty)

    let getUserProfile dbClient username httpContext =
        withToken httpContext
            (fun token -> currentUserByEmail dbClient username)

    let createNewArticle dbCLient httpContext =
        Auth.useToken httpContext (fun token ->
            async {
                try
                    let newArticle =
                        (JsonConvert.DeserializeObject<Article>
                            (httpContext.request.rawForm
                             |> Encoding.UTF8.GetString))

                    let checkedArticle =
                        newArticle
                        |> Jernl.Convert.checkNullAuthor
                        |> Jernl.Convert.checkNullSlug
                        |> Jernl.Convert.checkFavoriteIds
                        |> Jernl.Convert.addDefaultSlug
                    insertNewArticle checkedArticle dbCLient |> ignore
                    return! Successful.OK (checkedArticle |> jsonToString)
                                httpContext
                with ex ->
                    return! NOT_FOUND "Database not available" httpContext
            })

    let getArticlesBy slug dbClient =
        getArticleBySlug dbClient slug
        |> Jernl.Convert.extractArticleList
        |> jsonToString
        |> Successful.OK

    let defaultTagsIfEmpty =
        function
        | Some tags -> tags
        | None -> { tags = [||] }

    let defaultArticleIfEmpty =
        function
        | Some articles -> Array.ofList articles
        | None -> [||]

    let getTagList dbClient =
        getSavedTagList dbClient
        |> defaultTagsIfEmpty
        |> jsonToString
        |> Successful.OK

    let chooseQuery (ctx : HttpContext) =
        sprintf "{%s%s%s}"
            (extractStringQueryVal ctx.request "tag" tagQueryPart)
            (extractStringQueryVal ctx.request "author" authorQueryPart)
            (extractStringQueryVal ctx.request "favorited" favoriteQueryPart)

    let getArticles dbClient httpContext =
        Auth.useToken httpContext (fun token ->
            async {
                try
                    let options =
                        (extractNumericQueryVal httpContext.request "limit",
                         extractNumericQueryVal httpContext.request "offset")

                    let articles =
                        match options with
                        | (limitAmount, _) when limitAmount > 0 ->
                            getSavedArticles dbClient (chooseQuery httpContext)
                                (Limit limitAmount)
                            |> Jernl.BsonDocConverter.toArticleList
                            |> jsonToString
                        | (_, offsetAmount) when offsetAmount > 0 ->
                            getSavedArticles dbClient (chooseQuery httpContext)
                                (Offset offsetAmount)
                            |> Jernl.BsonDocConverter.toArticleList
                            |> jsonToString
                        | _ ->
                            getSavedArticles dbClient (chooseQuery httpContext)
                                (Neither)
                            |> Jernl.BsonDocConverter.toArticleList
                            |> jsonToString
                    return! Successful.OK articles httpContext
                with ex ->
                    return! NOT_FOUND "Database not available" httpContext
            })

    let getArticlesForFeed dbClient httpContext =
        withToken httpContext (fun token ->
            getSavedFollowedArticles dbClient
            |> defaultArticleIfEmpty
            |> jsonToString)

    let addArticleWithSlug json (slug : string)
        (dbClient : MongoDB.Driver.IMongoDatabase) =
        let currentArticle = json |> Suave.Json.fromJson<Article>
        let updatedSlug = { currentArticle.article with slug = slug }
        insertNewArticle ({ currentArticle with article = updatedSlug })
            dbClient
        |> jsonToString
        |> Successful.OK

    let deleteArticleBy slug dbClient httpContext =
        withToken httpContext (fun token ->
            deleteArticleBySlug slug dbClient |> ignore
            String.Empty)

    let addCommentBy slug dbClient httpContext =
        withToken httpContext
            (fun token ->
            let json = httpContext.request.rawForm |> Encoding.UTF8.GetString
            let possibleArticleId = getArticleBySlug dbClient slug
            match possibleArticleId with
            | Some articleId ->
                saveNewComment
                    (JsonConvert.DeserializeObject<RequestComment> json)
                    (articleId.Id.ToString()) dbClient |> ignore
                json |> jsonToString
            | None ->
                { errors = { body = [| "Could not find article by slug" |] } }
                |> jsonToString)

    let getCommentsBySlug slug dbClient httpContext =
        withToken httpContext
            (fun token ->
            getCommentsFromArticlesBySlug slug dbClient |> jsonToString)

    let deleteComment (_, (id : string)) dbCLient httpContext =
        withToken httpContext (fun token ->
            deleteWithCommentId id dbCLient |> ignore
            String.Empty)

    let favoriteArticle slug dbClient httpContext =
        withToken httpContext (fun token ->
            favoriteArticleForUser dbClient token.UserName slug |> ignore
            String.Empty)

    let removeFavoriteCurrentUser slug dbClient httpContext =
        withToken httpContext (fun token ->
            removeFavoriteArticleFromUser dbClient token.UserName slug |> ignore
            String.Empty)

    let getFollowedProfile dbClient username httpContext =
        withToken httpContext (fun token ->
            followUser dbClient token.UserName username
            |> Convert.userToProfile
            |> jsonToString)

    let removeFollowedProfile dbClient username httpContext =
        withToken httpContext (fun token ->
            unfollowUser dbClient token.UserName username
            |> Convert.userToProfile
            |> jsonToString)
