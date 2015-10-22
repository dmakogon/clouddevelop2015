# Workshop walkthrough

## Introduction

This workshop is designed to help developers with Relational Database backgrounds
learn about Document Database data considerations.

We'll take a very simple relational model and walk through several refactorings
as we develop a Document model.

For the workshop, we'll tackle a Task database. And to make it a bit more interesting,
it has a bunch of tables and normalization that goes beyond the basic task list:

 Table | description
 ------|------------
 Person | Registered people
 Category | Task categories
 Status | Task status
 Task | The actual task itself, with foreign keys to Person and Status
 TaskNotes | Optional notes for a task (A person may enter any number of notes)
 TaskCategories | categories for each task (a task may have multiple categories)

## Relational queries

To start, it's helpful to see the types of queries are needed to render task details.

### Getting all tasks

Here, we'll get all tasks for a given person, along with description, categories
 and status.

```
select p.username, t.description, c.CategoryName, s.description from Person p
inner join task t on p.Id=t.PersonId
inner join TaskCategories tc on tc.TaskId = t.Id
inner join Category c on tc.CategoryId = c.Id
inner join Status s on t.TaskStatusId = s.Id
where p.username='David'
```