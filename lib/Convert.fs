namespace Jernl

module Convert =
    open Jernl.Models
    open MongoDB.Driver

    let userRequestToUser (user : UserRequest) =
        { user =
              { username = user.user.username
                email = user.user.email
                passwordhash = ""
                token = ""
                bio = ""
                image = ""
                favorites = [||]
                following = [||] }
          Id = "" }

    let userToProfile (user : User) =
        { profile =
              { username = user.user.username
                bio = user.user.bio
                image = user.user.image
                following = true } }

    let updateUser (user : UserDetails) (result : UpdateResult option) : string =
        match result with
        | Some _ ->
            user
            |> Suave.Json.toJson
            |> System.Text.Encoding.UTF8.GetString
        | None ->
            { errors = { body = [| "Error updating this user." |] } }
            |> Suave.Json.toJson
            |> System.Text.Encoding.UTF8.GetString

    let defaultProfile =
        { username = ""
          bio = ""
          image = ""
          following = false }

    let defaultArticle =
        { Id = ""
          article =
              { slug = ""
                title = ""
                description = ""
                body = ""
                createdAt = System.DateTime.Now
                updatedAt = System.DateTime.Now
                favoriteIds = [||]
                favorited = false
                favoritesCount = 0u
                author = defaultProfile
                tagList = [||] } }

    let extractArticleList result =
        match result with
        | Some article -> article
        | None -> defaultArticle

    let defaultAuthor =
        { username = ""
          bio = ""
          image = ""
          following = false }

    let checkNullAuthor (art : Article) =
        match obj.ReferenceEquals(art.article.author, null) with
        | true ->
            { art with article = { art.article with author = defaultAuthor } }
        | _ -> art

    let checkNullSlug (art : Article) =
        match obj.ReferenceEquals(art.article.slug, null) with
        | true -> { art with article = { art.article with slug = "" } }
        | _ -> art

    let checkNullString field =
        match obj.ReferenceEquals(field, null) with
        | true -> ""
        | _ -> field

    let checkFavoriteIds (art : Article) =
        match obj.ReferenceEquals(art.article.favoriteIds, null) with
        | true -> { art with article = { art.article with favoriteIds = [||] } }
        | _ -> art

    let addDefaultSlug (art : Article) =
        match String.isEmpty art.article.slug with
        | true ->
            let wordSections =
                art.article.title.Split()
                |> Array.map (fun word -> word.ToLower().Trim())
                |> String.concat "-"
            { art with article = { art.article with slug = wordSections } }
        | _ -> art
